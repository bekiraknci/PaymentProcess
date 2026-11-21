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

    public void Add(Payment payment) => db.Payments.Add(payment);
}
