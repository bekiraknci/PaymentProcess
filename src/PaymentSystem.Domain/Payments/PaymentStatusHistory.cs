using PaymentSystem.Domain.Common;

namespace PaymentSystem.Domain.Payments;

public sealed class PaymentStatusHistory : Entity
{
    private PaymentStatusHistory()
    {
    }

    internal PaymentStatusHistory(
        Guid paymentId,
        PaymentStatus? fromStatus,
        PaymentStatus toStatus,
        string? reason,
        DateTimeOffset changedAt)
        : base(Guid.CreateVersion7(changedAt))
    {
        PaymentId = paymentId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        Reason = reason;
        ChangedAt = changedAt;
    }

    public Guid PaymentId { get; private set; }
    public PaymentStatus? FromStatus { get; private set; }
    public PaymentStatus ToStatus { get; private set; }
    public string? Reason { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string? ChangedBy { get; private set; }
}
