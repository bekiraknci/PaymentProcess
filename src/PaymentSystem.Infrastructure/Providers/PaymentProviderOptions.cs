using System.ComponentModel.DataAnnotations;

namespace PaymentSystem.Infrastructure.Providers;

public sealed class PaymentProviderOptions
{
    public const string SectionName = "PaymentProvider";

    [Required]
    [Url]
    public string BaseUrl { get; init; } = "http://localhost:5100";

    [Range(1, 120)]
    public int AttemptTimeoutSeconds { get; init; } = 5;

    [Range(1, 600)]
    public int TotalTimeoutSeconds { get; init; } = 20;

    [Range(0, 10)]
    public int MaxRetryAttempts { get; init; } = 3;

    [Range(2, 100_000)]
    public int CircuitBreakerMinimumThroughput { get; init; } = 5;

    [Range(1, 3600)]
    public int CircuitBreakerBreakSeconds { get; init; } = 15;
}
