using FluentValidation;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Common.Results;
using PaymentSystem.Application.Common.Validation;

namespace PaymentSystem.Application.Reports;

public sealed record DailyPaymentReportRow(
    DateOnly Date,
    Guid ProviderId,
    string ProviderName,
    string Status,
    string Currency,
    long PaymentCount,
    decimal TotalAmount);

public sealed record CurrencyTotal(string Currency, long PaymentCount, decimal TotalAmount);

public sealed record DailyPaymentReportResponse(
    DateOnly From,
    DateOnly To,
    Guid? ProviderId,
    IReadOnlyList<DailyPaymentReportRow> Rows,
    IReadOnlyList<CurrencyTotal> TotalsByCurrency);

public sealed record GetDailyPaymentReportQuery(DateOnly? From, DateOnly? To, Guid? ProviderId);

public interface IReportingQueries
{
    Task<IReadOnlyList<DailyPaymentReportRow>> GetDailyPaymentReportAsync(
        DateOnly from,
        DateOnly to,
        Guid? providerId,
        CancellationToken cancellationToken = default);
}

public sealed class GetDailyPaymentReportValidator : AbstractValidator<GetDailyPaymentReportQuery>
{
    public const int MaxRangeDays = 366;

    public GetDailyPaymentReportValidator()
    {
        RuleFor(x => x.From).NotNull();
        RuleFor(x => x.To).NotNull();

        RuleFor(x => x.To)
            .GreaterThanOrEqualTo(x => x.From)
            .WithMessage("'To' must be on or after 'From'.")
            .When(x => x.From.HasValue && x.To.HasValue);

        RuleFor(x => x)
            .Must(x => x.To!.Value.DayNumber - x.From!.Value.DayNumber < MaxRangeDays)
            .WithName("To")
            .OverridePropertyName("to")
            .WithMessage($"The report range can span at most {MaxRangeDays} days.")
            .When(x => x.From.HasValue && x.To.HasValue && x.To >= x.From);

        RuleFor(x => x.ProviderId)
            .NotEqual(Guid.Empty)
            .When(x => x.ProviderId.HasValue);
    }
}

public sealed class GetDailyPaymentReportHandler(
    IValidator<GetDailyPaymentReportQuery> validator,
    IReportingQueries reporting,
    IPaymentProviderRepository providers)
{
    public async Task<Result<DailyPaymentReportResponse>> HandleAsync(GetDailyPaymentReportQuery query, CancellationToken cancellationToken)
    {
        if (await validator.ValidateToErrorAsync(query, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        if (query.ProviderId is { } providerId && await providers.GetByIdAsync(providerId, cancellationToken) is null)
        {
            return Error.NotFound("provider.not_found", $"Payment provider '{providerId}' was not found.");
        }

        var rows = await reporting.GetDailyPaymentReportAsync(query.From!.Value, query.To!.Value, query.ProviderId, cancellationToken);

        var totals = rows
            .GroupBy(r => r.Currency)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new CurrencyTotal(g.Key, g.Sum(r => r.PaymentCount), g.Sum(r => r.TotalAmount)))
            .ToList();

        return new DailyPaymentReportResponse(query.From.Value, query.To.Value, query.ProviderId, rows, totals);
    }
}
