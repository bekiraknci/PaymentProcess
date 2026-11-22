using Microsoft.EntityFrameworkCore;
using PaymentSystem.Application.Reports;

namespace PaymentSystem.Infrastructure.Persistence.Queries;

internal sealed class ReportingQueries(AppDbContext db) : IReportingQueries
{
    public async Task<IReadOnlyList<DailyPaymentReportRow>> GetDailyPaymentReportAsync(
        DateOnly from,
        DateOnly to,
        Guid? providerId,
        CancellationToken cancellationToken = default)
    {
        var rows = await db.Database
            .SqlQuery<ReportRecord>(
                $"EXEC dbo.sp_GetDailyPaymentReport @From = {from}, @To = {to}, @ProviderId = {providerId}")
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new DailyPaymentReportRow(r.Date, r.ProviderId, r.ProviderName, r.Status, r.Currency.Trim(), r.PaymentCount, r.TotalAmount))
            .ToList();
    }

    private sealed class ReportRecord
    {
        public DateOnly Date { get; init; }
        public Guid ProviderId { get; init; }
        public string ProviderName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string Currency { get; init; } = string.Empty;
        public long PaymentCount { get; init; }
        [Precision(38, 2)]
        public decimal TotalAmount { get; init; }
    }
}
