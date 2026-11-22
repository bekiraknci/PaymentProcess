using System.ComponentModel.DataAnnotations;

namespace PaymentSystem.Application.Payments.Retry;

public sealed class RetryJobOptions
{
    public const string SectionName = "RetryJob";

    public bool Enabled { get; init; } = true;

    [Range(1, 86_400)]
    public int IntervalSeconds { get; init; } = 60;

    [Range(1, 10_000)]
    public int BatchSize { get; init; } = 100;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
}
