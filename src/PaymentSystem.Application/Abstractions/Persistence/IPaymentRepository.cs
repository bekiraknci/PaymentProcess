using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Abstractions.Persistence;

public interface IPaymentRepository
{
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Payment?> GetByIdempotencyKeyAsync(Guid userId, string idempotencyKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Guid>> GetPendingIdsAsync(int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Payment>> GetDueForAutomaticRetryAsync(DateTimeOffset now, int maxAttempts, int take, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Payment>> GetExpiredProcessingAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default);

    void Add(Payment payment);
}
