using PaymentSystem.Domain.Providers;

namespace PaymentSystem.Application.Providers;

public sealed record ProviderResponse(
    Guid Id,
    string Code,
    string Name,
    string? BaseUrl,
    bool IsActive,
    IReadOnlyList<string> SupportedCurrencies,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt)
{
    public static ProviderResponse From(PaymentProvider provider) => new(
        provider.Id,
        provider.Code,
        provider.Name,
        provider.BaseUrl,
        provider.IsActive,
        provider.SupportedCurrencies,
        provider.CreatedAt,
        provider.UpdatedAt);
}
