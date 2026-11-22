using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Payments.Processing;
using PaymentSystem.Application.Payments.Retry;
using PaymentSystem.Application.Tests.Fakes;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Tests.Payments;

public sealed class RetryDueFailedPaymentsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryPaymentRepository _payments = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly RecordingPaymentQueue _queue = new();
    private readonly FixedTimeProvider _time = new(Now);

    private RetryDueFailedPaymentsHandler CreateHandler(int batchSize = 100) => new(
        _payments,
        _unitOfWork,
        _queue,
        Options.Create(new PaymentProcessingOptions { MaxAutomaticAttempts = 3 }),
        Options.Create(new RetryJobOptions { BatchSize = batchSize }),
        _time,
        NullLogger<RetryDueFailedPaymentsHandler>.Instance);

    private Payment AddFailed(DateTimeOffset? nextAttemptAt, int attempts = 1)
    {
        var payment = Payment.Create(Guid.NewGuid(), Guid.NewGuid(), Money.Create(10m, "USD"), null, null, Now.AddHours(-1));
        for (var i = 0; i < attempts; i++)
        {
            if (i > 0)
            {
                payment.RequeueForRetry(Now.AddHours(-1));
            }

            payment.StartProcessing(Now.AddHours(-1), TimeSpan.FromMinutes(1));
            payment.Fail("Provider unavailable", i == attempts - 1 ? nextAttemptAt : Now.AddHours(-1), Now.AddHours(-1));
        }

        _payments.Add(payment);
        return payment;
    }

    [Fact]
    public async Task Requeues_only_failed_payments_that_are_due_and_under_the_attempt_cap()
    {
        var due = AddFailed(Now.AddSeconds(-1));
        var notDue = AddFailed(Now.AddMinutes(5));
        var noSchedule = AddFailed(null);
        var exhausted = AddFailed(Now.AddSeconds(-1), attempts: 3);

        var count = await CreateHandler().HandleAsync(CancellationToken.None);

        count.Should().Be(1);
        due.Status.Should().Be(PaymentStatus.Pending);
        notDue.Status.Should().Be(PaymentStatus.Failed);
        noSchedule.Status.Should().Be(PaymentStatus.Failed);
        exhausted.Status.Should().Be(PaymentStatus.Failed);
        _queue.Enqueued.Should().Equal(due.Id);
        _unitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Respects_the_batch_size()
    {
        for (var i = 0; i < 5; i++)
        {
            AddFailed(Now.AddSeconds(-1 - i));
        }

        var count = await CreateHandler(batchSize: 2).HandleAsync(CancellationToken.None);

        count.Should().Be(2);
        _queue.Enqueued.Should().HaveCount(2);
    }

    [Fact]
    public async Task Nothing_due_does_not_touch_the_database()
    {
        AddFailed(Now.AddMinutes(5));

        var count = await CreateHandler().HandleAsync(CancellationToken.None);

        count.Should().Be(0);
        _unitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_change_skips_the_tick_without_enqueueing()
    {
        AddFailed(Now.AddSeconds(-1));
        _unitOfWork.BeforeSave = () => new ConcurrencyConflictException("changed", new Exception());

        var count = await CreateHandler().HandleAsync(CancellationToken.None);

        count.Should().Be(0);
        _queue.Enqueued.Should().BeEmpty();
    }
}
