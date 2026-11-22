using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Abstractions.Providers;
using PaymentSystem.Application.Common.Results;
using PaymentSystem.Application.Payments.Processing;
using PaymentSystem.Application.Payments.RetryPayment;
using PaymentSystem.Application.Tests.Fakes;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Tests.Payments;

public sealed class ProcessPaymentHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPaymentRepository _payments = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly StubProviderClient _provider = new(ProviderChargeResult.Approved("txn_1"));
    private readonly FixedTimeProvider _time = new(Now);
    private readonly PaymentProcessingOptions _options = new() { MaxAutomaticAttempts = 3, RetryBaseDelaySeconds = 60 };

    private ProcessPaymentHandler CreateHandler() => new(
        _payments,
        _unitOfWork,
        _provider,
        Options.Create(_options),
        _time,
        NullLogger<ProcessPaymentHandler>.Instance);

    private Payment AddPendingPayment()
    {
        var payment = Payment.Create(Guid.NewGuid(), Guid.NewGuid(), Money.Create(100m, "USD"), null, null, Now);
        _payments.Add(payment);
        return payment;
    }

    [Fact]
    public async Task Approved_charge_completes_the_payment()
    {
        var payment = AddPendingPayment();

        var outcome = await CreateHandler().HandleAsync(payment.Id, CancellationToken.None);

        outcome.Should().Be(ProcessPaymentOutcome.Completed);
        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.ProviderTransactionId.Should().Be("txn_1");
        payment.AttemptCount.Should().Be(1);
        _provider.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Processing_the_same_payment_twice_is_a_no_op()
    {
        var payment = AddPendingPayment();
        var handler = CreateHandler();

        await handler.HandleAsync(payment.Id, CancellationToken.None);
        var second = await handler.HandleAsync(payment.Id, CancellationToken.None);

        second.Should().Be(ProcessPaymentOutcome.Skipped);
        _provider.Calls.Should().Be(1);
        payment.AttemptCount.Should().Be(1);
        payment.Status.Should().Be(PaymentStatus.Completed);
    }

    [Fact]
    public async Task Losing_the_claim_race_skips_without_calling_the_provider()
    {
        var payment = AddPendingPayment();
        _unitOfWork.BeforeSave = () => new ConcurrencyConflictException("claimed elsewhere", new Exception());

        var outcome = await CreateHandler().HandleAsync(payment.Id, CancellationToken.None);

        outcome.Should().Be(ProcessPaymentOutcome.Skipped);
        _provider.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Declined_charge_fails_with_exponential_backoff()
    {
        var payment = AddPendingPayment();
        _provider.Result = ProviderChargeResult.Declined("insufficient_funds");

        var outcome = await CreateHandler().HandleAsync(payment.Id, CancellationToken.None);

        outcome.Should().Be(ProcessPaymentOutcome.Failed);
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Contain("insufficient_funds");
        payment.NextAttemptAt.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public async Task Open_circuit_postpones_the_next_attempt_until_the_circuit_can_close()
    {
        var payment = AddPendingPayment();
        _provider.Result = ProviderChargeResult.Unavailable("Provider circuit is open.", TimeSpan.FromMinutes(10));

        await CreateHandler().HandleAsync(payment.Id, CancellationToken.None);

        payment.NextAttemptAt.Should().Be(Now.AddMinutes(10));
    }

    [Fact]
    public async Task Retry_after_shorter_than_backoff_keeps_the_backoff()
    {
        var payment = AddPendingPayment();
        _provider.Result = ProviderChargeResult.Unavailable("Provider circuit is open.", TimeSpan.FromSeconds(15));

        await CreateHandler().HandleAsync(payment.Id, CancellationToken.None);

        payment.NextAttemptAt.Should().Be(Now.AddMinutes(2));
    }

    [Fact]
    public async Task Last_automatic_attempt_leaves_no_next_attempt()
    {
        var payment = AddPendingPayment();
        _provider.Result = ProviderChargeResult.Unavailable("503");
        var handler = CreateHandler();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            await handler.HandleAsync(payment.Id, CancellationToken.None);
            if (attempt < 3)
            {
                payment.NextAttemptAt.Should().NotBeNull();
                payment.RequeueForRetry(Now);
            }
        }

        payment.AttemptCount.Should().Be(3);
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.NextAttemptAt.Should().BeNull();
        payment.CanAutoRetry(_options.MaxAutomaticAttempts).Should().BeFalse();
    }

    [Fact]
    public async Task Manual_retry_requeues_failed_payment()
    {
        var payment = AddPendingPayment();
        payment.StartProcessing(Now, TimeSpan.FromMinutes(1));
        payment.Fail("Declined", null, Now);
        var queue = new RecordingPaymentQueue();

        var result = await new RetryPaymentHandler(_payments, _unitOfWork, queue, _time).HandleAsync(payment.Id, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        payment.Status.Should().Be(PaymentStatus.Pending);
        queue.Enqueued.Should().Equal(payment.Id);
    }

    [Fact]
    public async Task Manual_retry_of_non_failed_payment_is_a_conflict()
    {
        var payment = AddPendingPayment();

        var result = await new RetryPaymentHandler(_payments, _unitOfWork, new RecordingPaymentQueue(), _time)
            .HandleAsync(payment.Id, CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task Manual_retry_of_unknown_payment_is_not_found()
    {
        var result = await new RetryPaymentHandler(_payments, _unitOfWork, new RecordingPaymentQueue(), _time)
            .HandleAsync(Guid.NewGuid(), CancellationToken.None);

        result.Error!.Type.Should().Be(ErrorType.NotFound);
    }

    private sealed class DelegateProviderClient(Func<ProviderChargeResult> charge) : IPaymentProviderClient
    {
        public Task<ProviderChargeResult> ChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(charge());
    }

    private ProcessPaymentHandler CreateHandler(IPaymentProviderClient client) => new(
        _payments,
        _unitOfWork,
        client,
        Options.Create(_options),
        _time,
        NullLogger<ProcessPaymentHandler>.Instance);

    [Fact]
    public async Task Unexpected_provider_exception_fails_the_payment_instead_of_leaving_it_processing()
    {
        var payment = AddPendingPayment();
        var handler = CreateHandler(new DelegateProviderClient(() => throw new InvalidOperationException("boom")));

        var outcome = await handler.HandleAsync(payment.Id, CancellationToken.None);

        outcome.Should().Be(ProcessPaymentOutcome.Failed);
        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Contain("Unexpected error");
        payment.NextAttemptAt.Should().NotBeNull();
        payment.LockedUntil.Should().BeNull();
    }

    [Fact]
    public async Task Shutdown_during_provider_call_propagates_and_leaves_the_lease_for_recovery()
    {
        var payment = AddPendingPayment();
        using var cts = new CancellationTokenSource();
        var handler = CreateHandler(new DelegateProviderClient(() =>
        {
            cts.Cancel();
            throw new OperationCanceledException(cts.Token);
        }));

        var act = () => handler.HandleAsync(payment.Id, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        payment.Status.Should().Be(PaymentStatus.Processing);
    }

    [Fact]
    public async Task Concurrent_change_while_the_charge_is_in_flight_is_skipped_without_throwing()
    {
        var payment = AddPendingPayment();
        var handler = CreateHandler(new DelegateProviderClient(() =>
        {
            _unitOfWork.BeforeSave = () => new ConcurrencyConflictException("changed", new Exception());
            return ProviderChargeResult.Approved("txn_late");
        }));

        var outcome = await handler.HandleAsync(payment.Id, CancellationToken.None);

        outcome.Should().Be(ProcessPaymentOutcome.Skipped);
        _unitOfWork.SaveCount.Should().Be(1);
    }
}
