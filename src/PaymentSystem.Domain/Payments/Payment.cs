using PaymentSystem.Domain.Common;
using PaymentSystem.Domain.Payments.Events;

namespace PaymentSystem.Domain.Payments;

public sealed class Payment : AggregateRoot
{
    public const int IdempotencyKeyMaxLength = 128;
    public const int FailureReasonMaxLength = 512;

    private readonly List<PaymentStatusHistory> _history = [];

    private Payment()
    {
        Money = null!;
        RowVersion = [];
    }

    private Payment(Guid id, Guid userId, Guid providerId, Money money, string? idempotencyKey, string? requestHash)
        : base(id)
    {
        UserId = userId;
        ProviderId = providerId;
        Money = money;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        RowVersion = [];
    }

    public Guid UserId { get; private set; }
    public Guid ProviderId { get; private set; }
    public Money Money { get; private set; }
    public PaymentStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAt { get; private set; }
    public DateTimeOffset? LockedUntil { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? RequestHash { get; private set; }
    public string? ProviderTransactionId { get; private set; }
    public string? FailureReason { get; private set; }
    public byte[] RowVersion { get; private set; }

    public IReadOnlyCollection<PaymentStatusHistory> History => _history.AsReadOnly();

    public static Payment Create(
        Guid userId,
        Guid providerId,
        Money money,
        string? idempotencyKey,
        string? requestHash,
        DateTimeOffset now)
    {
        if (userId == Guid.Empty)
        {
            throw new DomainException("payment.user_required", "Payment must belong to a user.");
        }

        if (providerId == Guid.Empty)
        {
            throw new DomainException("payment.provider_required", "Payment must have a provider.");
        }

        ArgumentNullException.ThrowIfNull(money);

        if (idempotencyKey is { Length: > IdempotencyKeyMaxLength })
        {
            throw new DomainException("payment.idempotency_key_too_long", $"Idempotency key can be at most {IdempotencyKeyMaxLength} characters.");
        }

        var payment = new Payment(Guid.CreateVersion7(now), userId, providerId, money, idempotencyKey, requestHash);
        payment.RecordTransition(null, PaymentStatus.Pending, "Payment created.", now);
        return payment;
    }

    public void StartProcessing(DateTimeOffset now, TimeSpan leaseDuration)
    {
        EnsureStatus(PaymentStatus.Pending, nameof(StartProcessing));

        if (leaseDuration <= TimeSpan.Zero)
        {
            throw new DomainException("payment.invalid_lease", "Lease duration must be positive.");
        }

        AttemptCount++;
        LockedUntil = now.Add(leaseDuration);
        NextAttemptAt = null;
        TransitionTo(PaymentStatus.Processing, $"Processing attempt #{AttemptCount} started.", now);
    }

    public void Complete(string providerTransactionId, DateTimeOffset now)
    {
        EnsureStatus(PaymentStatus.Processing, nameof(Complete));

        if (string.IsNullOrWhiteSpace(providerTransactionId))
        {
            throw new DomainException("payment.transaction_id_required", "Provider transaction id is required.");
        }

        ProviderTransactionId = providerTransactionId;
        FailureReason = null;
        LockedUntil = null;
        NextAttemptAt = null;
        TransitionTo(PaymentStatus.Completed, "Approved by provider.", now);
    }

    public void Fail(string reason, DateTimeOffset? nextAttemptAt, DateTimeOffset now)
    {
        EnsureStatus(PaymentStatus.Processing, nameof(Fail));

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("payment.failure_reason_required", "Failure reason is required.");
        }

        FailureReason = reason.Length > FailureReasonMaxLength ? reason[..FailureReasonMaxLength] : reason;
        LockedUntil = null;
        NextAttemptAt = nextAttemptAt;
        TransitionTo(PaymentStatus.Failed, FailureReason, now);
    }

    public void RequeueForRetry(DateTimeOffset now, string reason = "Requeued for retry.")
    {
        EnsureStatus(PaymentStatus.Failed, nameof(RequeueForRetry));

        NextAttemptAt = null;
        TransitionTo(PaymentStatus.Pending, reason, now);
    }

    public void RequeueForAutomaticRetry(DateTimeOffset now, int maxAttempts)
    {
        if (!CanAutoRetry(maxAttempts))
        {
            throw new DomainException(
                "payment.auto_retry_exhausted",
                $"Payment cannot be retried automatically (status '{Status}', attempts {AttemptCount}/{maxAttempts}).");
        }

        if (NextAttemptAt is not { } due || due > now)
        {
            throw new DomainException("payment.auto_retry_not_due", "Payment is not due for an automatic retry yet.");
        }

        RequeueForRetry(now, $"Automatic retry scheduled (attempt {AttemptCount + 1} of {maxAttempts}).");
    }

    public void ReleaseExpiredLease(DateTimeOffset now)
    {
        EnsureStatus(PaymentStatus.Processing, nameof(ReleaseExpiredLease));

        if (LockedUntil is { } lockedUntil && lockedUntil > now)
        {
            throw new DomainException("payment.lease_active", "Processing lease has not expired yet.");
        }

        LockedUntil = null;
        TransitionTo(PaymentStatus.Pending, "Processing lease expired; returned to queue.", now);
    }

    public bool CanAutoRetry(int maxAttempts) =>
        Status == PaymentStatus.Failed && AttemptCount < maxAttempts;

    public bool IsLeaseExpired(DateTimeOffset now) =>
        Status == PaymentStatus.Processing && (LockedUntil is null || LockedUntil <= now);

    private void EnsureStatus(PaymentStatus expected, string operation)
    {
        if (Status != expected)
        {
            throw new DomainException(
                "payment.invalid_status_transition",
                $"Cannot {operation} a payment in '{Status}' status. Expected '{expected}'.");
        }
    }

    private void TransitionTo(PaymentStatus target, string? reason, DateTimeOffset now) =>
        RecordTransition(Status, target, reason, now);

    private void RecordTransition(PaymentStatus? from, PaymentStatus target, string? reason, DateTimeOffset now)
    {
        Status = target;
        _history.Add(new PaymentStatusHistory(Id, from, target, reason, now));
        Raise(new PaymentStatusChangedDomainEvent(Id, from, target, reason, now));
    }
}
