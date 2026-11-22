namespace PaymentSystem.Api.Contracts;

/// <summary>Credentials for obtaining an access token.</summary>
public sealed class LoginRequest
{
    /// <summary>User email.</summary>
    /// <example>user@test.com</example>
    public string Email { get; init; } = string.Empty;

    /// <summary>User password.</summary>
    /// <example>User123!</example>
    public string Password { get; init; } = string.Empty;
}

/// <summary>Payment submission request.</summary>
public sealed class CreatePaymentRequest
{
    /// <summary>Identifier of an active payment provider.</summary>
    /// <example>0196a000-0000-7000-8000-000000000001</example>
    public Guid ProviderId { get; init; }

    /// <summary>Amount greater than zero with at most two decimal places. Use 666, 503 or 999 to trigger provider failures.</summary>
    /// <example>125.50</example>
    public decimal Amount { get; init; }

    /// <summary>ISO 4217 currency code: TRY, USD, EUR or GBP.</summary>
    /// <example>USD</example>
    public string Currency { get; init; } = string.Empty;
}

/// <summary>New payment provider.</summary>
public sealed class CreateProviderRequest
{
    /// <summary>Unique code; letters, digits, '_' or '-'. Stored in upper case.</summary>
    /// <example>ACMEPAY</example>
    public string Code { get; init; } = string.Empty;

    /// <summary>Display name.</summary>
    /// <example>Acme Pay</example>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional absolute http(s) base URL.</summary>
    /// <example>https://api.acmepay.test</example>
    public string? BaseUrl { get; init; }

    /// <summary>Currencies the provider accepts.</summary>
    /// <example>["USD", "EUR"]</example>
    public IReadOnlyList<string> SupportedCurrencies { get; init; } = [];

    /// <summary>Whether the provider accepts new payments immediately.</summary>
    /// <example>true</example>
    public bool IsActive { get; init; } = true;
}

/// <summary>Updated provider details.</summary>
public sealed class UpdateProviderRequest
{
    /// <summary>Display name.</summary>
    /// <example>Acme Pay Europe</example>
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional absolute http(s) base URL.</summary>
    /// <example>https://eu.acmepay.test</example>
    public string? BaseUrl { get; init; }

    /// <summary>Currencies the provider accepts.</summary>
    /// <example>["EUR", "GBP"]</example>
    public IReadOnlyList<string> SupportedCurrencies { get; init; } = [];
}
