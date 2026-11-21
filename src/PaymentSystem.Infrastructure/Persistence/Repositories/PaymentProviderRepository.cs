using Microsoft.EntityFrameworkCore;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Domain.Providers;

namespace PaymentSystem.Infrastructure.Persistence.Repositories;

internal sealed class PaymentProviderRepository(AppDbContext db) : IPaymentProviderRepository
{
    public Task<PaymentProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        db.PaymentProviders.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return db.PaymentProviders.AnyAsync(p => p.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<PaymentProvider>> ListAsync(CancellationToken cancellationToken = default) =>
        await db.PaymentProviders
            .AsNoTracking()
            .OrderBy(p => p.Code)
            .ToListAsync(cancellationToken);

    public void Add(PaymentProvider provider) => db.PaymentProviders.Add(provider);
}
