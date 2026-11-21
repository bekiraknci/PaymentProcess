using PaymentSystem.Domain.Common;

namespace PaymentSystem.Domain.Payments.Events;

public sealed record PaymentStatusChangedDomainEvent(
    Guid PaymentId,
    PaymentStatus? FromStatus,
    PaymentStatus ToStatus,
    string? Reason,
    DateTimeOffset OccurredAt) : IDomainEvent;
