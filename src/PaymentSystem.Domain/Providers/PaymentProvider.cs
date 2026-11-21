using PaymentSystem.Domain.Common;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Domain.Providers;

public sealed class PaymentProvider : AggregateRoot
{
    public const int CodeMaxLength = 32;
    public const int NameMaxLength = 128;
    public const int BaseUrlMaxLength = 512;
    private const char CurrencySeparator = ',';

    private string _supportedCurrencies = string.Empty;

    private PaymentProvider()
    {
        Code = null!;
        Name = null!;
    }

    private PaymentProvider(Guid id, string code, string name) : base(id)
    {
        Code = code;
        Name = name;
    }

    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? BaseUrl { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyList<string> SupportedCurrencies =>
        _supportedCurrencies.Split(CurrencySeparator, StringSplitOptions.RemoveEmptyEntries);

    public static PaymentProvider Create(
        string code,
        string name,
        string? baseUrl,
        IEnumerable<string> supportedCurrencies,
        bool isActive,
        DateTimeOffset now)
    {
        var provider = new PaymentProvider(Guid.CreateVersion7(now), NormalizeCode(code), string.Empty);
        provider.Update(name, baseUrl, supportedCurrencies);
        provider.IsActive = isActive;
        return provider;
    }

    public void Update(string name, string? baseUrl, IEnumerable<string> supportedCurrencies)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength)
        {
            throw new DomainException("provider.invalid_name", $"Provider name is required and can be at most {NameMaxLength} characters.");
        }

        if (baseUrl is not null && (baseUrl.Length > BaseUrlMaxLength || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _)))
        {
            throw new DomainException("provider.invalid_base_url", "Provider base URL must be an absolute URL.");
        }

        var currencies = supportedCurrencies
            .Select(c => c?.Trim().ToUpperInvariant() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (currencies.Count == 0)
        {
            throw new DomainException("provider.no_currency", "Provider must support at least one currency.");
        }

        var unsupported = currencies.Where(c => !Currency.IsSupported(c)).ToList();
        if (unsupported.Count > 0)
        {
            throw new DomainException("provider.unsupported_currency", $"Unsupported currencies: {string.Join(", ", unsupported)}.");
        }

        Name = name.Trim();
        BaseUrl = string.IsNullOrWhiteSpace(baseUrl) ? null : baseUrl;
        _supportedCurrencies = string.Join(CurrencySeparator, currencies.Order(StringComparer.Ordinal));
    }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    public bool SupportsCurrency(string currency) =>
        !string.IsNullOrWhiteSpace(currency) &&
        SupportedCurrencies.Contains(Currency.Normalize(currency), StringComparer.Ordinal);

    public bool CanAccept(Money money) => IsActive && SupportsCurrency(money.Currency);

    private static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("provider.code_required", "Provider code is required.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length > CodeMaxLength || !normalized.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-'))
        {
            throw new DomainException("provider.invalid_code", $"Provider code can contain letters, digits, '_' or '-' and be at most {CodeMaxLength} characters.");
        }

        return normalized;
    }
}
