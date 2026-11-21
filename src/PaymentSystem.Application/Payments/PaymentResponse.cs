using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Payments;

public sealed record PaymentResponse(
    Guid Id,
    Guid UserId,
    Guid ProviderId,
    decimal Amount,
    string Currency,
    PaymentStatus Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static PaymentResponse From(Payment payment) => new(
        payment.Id,
        payment.UserId,
        payment.ProviderId,
        payment.Money.Amount,
        payment.Money.Currency,
        payment.Status,
        payment.AttemptCount,
        payment.NextAttemptAt,
        payment.ProviderTransactionId,
        payment.FailureReason,
        payment.CreatedAt,
        payment.UpdatedAt);
}

public sealed record PaymentHistoryResponse(
    PaymentStatus? FromStatus,
    PaymentStatus ToStatus,
    string? Reason,
    DateTimeOffset ChangedAt,
    string? ChangedBy);
