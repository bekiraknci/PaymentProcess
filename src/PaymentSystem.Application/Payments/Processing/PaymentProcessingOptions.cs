using System.ComponentModel.DataAnnotations;

namespace PaymentSystem.Application.Payments.Processing;

public sealed class PaymentProcessingOptions
{
    public const string SectionName = "PaymentProcessing";

    [Range(1, 32)]
    public int WorkerCount { get; init; } = 2;

    [Range(10, 3600)]
    public int LeaseSeconds { get; init; } = 120;

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; init; } = 10;

    [Range(1, 1000)]
    public int RecoveryBatchSize { get; init; } = 100;

    [Range(100, 1_000_000)]
    public int QueueCapacity { get; init; } = 10_000;

    [Range(1, 10)]
    public int MaxAutomaticAttempts { get; init; } = 3;

    [Range(1, 86_400)]
    public int RetryBaseDelaySeconds { get; init; } = 60;

    public TimeSpan Lease => TimeSpan.FromSeconds(LeaseSeconds);

    public DateTimeOffset? NextAttemptAfter(int attemptCount, DateTimeOffset now) =>
        attemptCount < MaxAutomaticAttempts
            ? now.AddSeconds(RetryBaseDelaySeconds * Math.Pow(2, attemptCount))
            : null;
}
