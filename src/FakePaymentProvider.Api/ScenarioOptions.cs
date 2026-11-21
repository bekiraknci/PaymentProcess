using System.ComponentModel.DataAnnotations;

namespace FakePaymentProvider.Api;

public sealed class ScenarioOptions
{
    public const string SectionName = "Scenarios";

    [Range(0, 60_000)]
    public int MinDelayMs { get; init; } = 100;

    [Range(0, 60_000)]
    public int MaxDelayMs { get; init; } = 800;

    [Range(0, 1)]
    public double DeclineRate { get; init; } = 0.1;

    [Range(0, 1)]
    public double TransientErrorRate { get; init; } = 0.1;

    [Range(0, 1)]
    public double TimeoutRate { get; init; } = 0.02;

    [Range(1_000, 600_000)]
    public int TimeoutDelayMs { get; init; } = 30_000;

    public decimal AlwaysDeclineAmount { get; init; } = 666m;

    public decimal AlwaysUnavailableAmount { get; init; } = 503m;

    public decimal AlwaysTimeoutAmount { get; init; } = 999m;
}
