namespace PaymentSystem.Domain.Payments;

public static class Currency
{
    public const string TRY = "TRY";
    public const string USD = "USD";
    public const string EUR = "EUR";
    public const string GBP = "GBP";

    public static IReadOnlySet<string> Supported { get; } =
        new HashSet<string>(StringComparer.Ordinal) { TRY, USD, EUR, GBP };

    public static bool IsSupported(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Supported.Contains(Normalize(code));

    public static string Normalize(string code) => code.Trim().ToUpperInvariant();
}
