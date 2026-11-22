using Microsoft.EntityFrameworkCore;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Infrastructure.Persistence.Repositories;

internal sealed class PaymentRepository(AppDbContext db) : IPaymentRepository
{
    public Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Payment?> GetByIdempotencyKeyAsync(Guid userId, string idempotencyKey, CancellationToken cancellationToken = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.UserId == userId && p.IdempotencyKey == idempotencyKey, cancellationToken);

    public async Task<IReadOnlyList<Guid>> GetPendingIdsAsync(int take, CancellationToken cancellationToken = default) =>
        await db.Payments
            .AsNoTracking()
            .Where(p => p.Status == PaymentStatus.Pending)
            .OrderBy(p => p.CreatedAt)
            .Select(p => p.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payment>> GetDueForAutomaticRetryAsync(DateTimeOffset now, int maxAttempts, int take, CancellationToken cancellationToken = default) =>
        await db.Payments
            .Where(p => p.Status == PaymentStatus.Failed && p.NextAttemptAt != null && p.NextAttemptAt <= now && p.AttemptCount < maxAttempts)
            .OrderBy(p => p.NextAttemptAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payment>> GetExpiredProcessingAsync(DateTimeOffset now, int take, CancellationToken cancellationToken = default) =>
        await db.Payments
            .Where(p => p.Status == PaymentStatus.Processing && (p.LockedUntil == null || p.LockedUntil <= now))
            .OrderBy(p => p.LockedUntil)
            .Take(take)
            .ToListAsync(cancellationToken);

    public void Add(Payment payment) => db.Payments.Add(payment);
}
