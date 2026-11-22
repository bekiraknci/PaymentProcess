using FluentAssertions;
using PaymentSystem.Domain.Common;
using PaymentSystem.Domain.Payments;
using PaymentSystem.Domain.Payments.Events;

namespace PaymentSystem.Domain.Tests.Payments;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(2);

    private static Payment NewPayment() =>
        Payment.Create(Guid.NewGuid(), Guid.NewGuid(), Money.Create(100m, "USD"), "key-1", new string('a', 64), Now);

    [Fact]
    public void Create_starts_in_pending_with_initial_history_and_event()
    {
        var payment = NewPayment();

        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.AttemptCount.Should().Be(0);
        payment.Id.Should().NotBeEmpty();
        payment.History.Should().ContainSingle()
            .Which.Should().Match<PaymentStatusHistory>(h => h.FromStatus == null && h.ToStatus == PaymentStatus.Pending);
        payment.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PaymentStatusChangedDomainEvent>();
    }

    [Fact]
    public void Create_requires_user_and_provider()
    {
        var noUser = () => Payment.Create(Guid.Empty, Guid.NewGuid(), Money.Create(1m, "USD"), null, null, Now);
        var noProvider = () => Payment.Create(Guid.NewGuid(), Guid.Empty, Money.Create(1m, "USD"), null, null, Now);

        noUser.Should().Throw<DomainException>();
        noProvider.Should().Throw<DomainException>();
    }

    [Fact]
    public void StartProcessing_increments_attempt_and_sets_lease()
    {
        var payment = NewPayment();

        payment.StartProcessing(Now, Lease);

        payment.Status.Should().Be(PaymentStatus.Processing);
        payment.AttemptCount.Should().Be(1);
        payment.LockedUntil.Should().Be(Now + Lease);
        payment.History.Last().FromStatus.Should().Be(PaymentStatus.Pending);
    }

    [Fact]
    public void Complete_from_processing_sets_transaction_and_clears_lease()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);

        payment.Complete("tx_123", Now.AddSeconds(1));

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.ProviderTransactionId.Should().Be("tx_123");
        payment.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void Fail_from_processing_records_reason_and_next_attempt()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);
        var next = Now.AddMinutes(2);

        payment.Fail("Declined", next, Now);

        payment.Status.Should().Be(PaymentStatus.Failed);
        payment.FailureReason.Should().Be("Declined");
        payment.NextAttemptAt.Should().Be(next);
        payment.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void RequeueForRetry_moves_failed_back_to_pending()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);
        payment.Fail("Declined", Now.AddMinutes(1), Now);

        payment.RequeueForRetry(Now.AddMinutes(1));

        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.NextAttemptAt.Should().BeNull();
        payment.AttemptCount.Should().Be(1);
    }

    [Fact]
    public void Completed_payment_cannot_be_processed_again()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);
        payment.Complete("tx_1", Now);

        var act = () => payment.StartProcessing(Now, Lease);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("payment.invalid_status_transition");
    }

    [Fact]
    public void Invalid_transitions_throw()
    {
        var pending = NewPayment();

        pending.Invoking(p => p.Complete("tx", Now)).Should().Throw<DomainException>();
        pending.Invoking(p => p.Fail("x", null, Now)).Should().Throw<DomainException>();
        pending.Invoking(p => p.RequeueForRetry(Now)).Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    public void CanAutoRetry_respects_max_attempts(int attempts, bool expected)
    {
        var payment = NewPayment();
        for (var i = 0; i < attempts; i++)
        {
            if (i > 0)
            {
                payment.RequeueForRetry(Now);
            }

            payment.StartProcessing(Now, Lease);
            payment.Fail("Declined", Now, Now);
        }

        payment.AttemptCount.Should().Be(attempts);
        payment.CanAutoRetry(maxAttempts: 3).Should().Be(expected);
    }

    [Fact]
    public void Automatic_retry_requeues_due_failed_payment_with_attempt_in_reason()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);
        payment.Fail("Provider unavailable", Now.AddMinutes(2), Now);

        payment.RequeueForAutomaticRetry(Now.AddMinutes(2), maxAttempts: 3);

        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.History.Last().Reason.Should().Contain("attempt 2 of 3");
    }

    [Fact]
    public void Automatic_retry_is_rejected_before_it_is_due()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);
        payment.Fail("Provider unavailable", Now.AddMinutes(2), Now);

        payment.Invoking(p => p.RequeueForAutomaticRetry(Now.AddMinutes(1), 3))
            .Should().Throw<DomainException>().Which.Code.Should().Be("payment.auto_retry_not_due");
    }

    [Fact]
    public void Automatic_retry_is_rejected_when_no_next_attempt_was_scheduled()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);
        payment.Fail("Provider unavailable", null, Now);

        payment.Invoking(p => p.RequeueForAutomaticRetry(Now.AddHours(1), 3))
            .Should().Throw<DomainException>().Which.Code.Should().Be("payment.auto_retry_not_due");
    }

    [Fact]
    public void Automatic_retry_is_rejected_when_attempts_are_exhausted()
    {
        var payment = NewPayment();
        for (var i = 0; i < 3; i++)
        {
            if (i > 0)
            {
                payment.RequeueForRetry(Now);
            }

            payment.StartProcessing(Now, Lease);
            payment.Fail("Declined", Now, Now);
        }

        payment.Invoking(p => p.RequeueForAutomaticRetry(Now.AddHours(1), 3))
            .Should().Throw<DomainException>().Which.Code.Should().Be("payment.auto_retry_exhausted");
    }

    [Fact]
    public void ReleaseExpiredLease_returns_stuck_payment_to_pending()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);

        payment.Invoking(p => p.ReleaseExpiredLease(Now.AddSeconds(30))).Should().Throw<DomainException>();

        payment.ReleaseExpiredLease(Now + Lease + TimeSpan.FromSeconds(1));

        payment.Status.Should().Be(PaymentStatus.Pending);
        payment.LockedUntil.Should().BeNull();
    }

    [Fact]
    public void Full_lifecycle_produces_ordered_history()
    {
        var payment = NewPayment();
        payment.StartProcessing(Now, Lease);
        payment.Fail("Provider unavailable", Now.AddMinutes(2), Now);
        payment.RequeueForRetry(Now.AddMinutes(2));
        payment.StartProcessing(Now.AddMinutes(2), Lease);
        payment.Complete("tx_9", Now.AddMinutes(3));

        payment.History.Select(h => h.ToStatus).Should().Equal(
            PaymentStatus.Pending,
            PaymentStatus.Processing,
            PaymentStatus.Failed,
            PaymentStatus.Pending,
            PaymentStatus.Processing,
            PaymentStatus.Completed);
        payment.AttemptCount.Should().Be(2);
    }
}
