using Microsoft.EntityFrameworkCore;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Common.Paging;
using PaymentSystem.Application.Payments;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Infrastructure.Persistence.Queries;

internal sealed class PaymentQueries(AppDbContext db) : IPaymentQueries
{
    public Task<PaymentResponse?> GetByIdAsync(Guid id, Guid? ownerId, CancellationToken cancellationToken = default) =>
        Visible(ownerId)
            .Where(p => p.Id == id)
            .Select(ToResponse)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<PaymentResponse>> ListAsync(
        Guid? ownerId,
        PaymentStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = Visible(ownerId);
        if (status is { } s)
        {
            query = query.Where(p => p.Status == s);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(ToResponse)
            .ToListAsync(cancellationToken);

        return new PagedResult<PaymentResponse>(items, page, pageSize, total);
    }

    public async Task<IReadOnlyList<PaymentHistoryResponse>?> GetHistoryAsync(Guid id, Guid? ownerId, CancellationToken cancellationToken = default)
    {
        if (!await Visible(ownerId).AnyAsync(p => p.Id == id, cancellationToken))
        {
            return null;
        }

        return await db.PaymentStatusHistory
            .AsNoTracking()
            .Where(h => h.PaymentId == id)
            .OrderBy(h => h.ChangedAt)
            .ThenBy(h => h.Id)
            .Select(h => new PaymentHistoryResponse(h.FromStatus, h.ToStatus, h.Reason, h.ChangedAt, h.ChangedBy))
            .ToListAsync(cancellationToken);
    }

    private IQueryable<Payment> Visible(Guid? ownerId)
    {
        var query = db.Payments.AsNoTracking();
        return ownerId is { } userId ? query.Where(p => p.UserId == userId) : query;
    }

    private static readonly System.Linq.Expressions.Expression<Func<Payment, PaymentResponse>> ToResponse = p =>
        new PaymentResponse(
            p.Id,
            p.UserId,
            p.ProviderId,
            p.Money.Amount,
            p.Money.Currency,
            p.Status,
            p.AttemptCount,
            p.NextAttemptAt,
            p.ProviderTransactionId,
            p.FailureReason,
            p.CreatedAt,
            p.UpdatedAt);
}
