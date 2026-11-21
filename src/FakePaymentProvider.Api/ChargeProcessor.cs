using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace FakePaymentProvider.Api;

public sealed record ChargeRequest(decimal Amount, string Currency, string? Reference);

public sealed record ChargeResponse(string Status, string? TransactionId, string? Reason);

public enum ChargeOutcome
{
    Approved,
    Declined,
    Unavailable,
    Timeout,
    KeyConflict
}

public sealed record ChargeResult(ChargeOutcome Outcome, ChargeResponse? Response = null);

public sealed class ChargeProcessor(IOptionsMonitor<ScenarioOptions> options, ILogger<ChargeProcessor> logger)
{
    private readonly ConcurrentDictionary<string, StoredCharge> _approved = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<ChargeResult> ProcessAsync(string idempotencyKey, ChargeRequest request, CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(request);
        var gate = _locks.GetOrAdd(idempotencyKey, _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (_approved.TryGetValue(idempotencyKey, out var stored))
            {
                if (stored.Fingerprint != fingerprint)
                {
                    return new ChargeResult(ChargeOutcome.KeyConflict);
                }

                logger.LogInformation("Replaying approved charge {TransactionId} for key {Key}.", stored.Response.TransactionId, idempotencyKey);
                return new ChargeResult(ChargeOutcome.Approved, stored.Response);
            }

            var result = await DecideAsync(request, cancellationToken);
            if (result.Outcome == ChargeOutcome.Approved)
            {
                _approved[idempotencyKey] = new StoredCharge(fingerprint, result.Response!);
            }

            logger.LogInformation("Charge {Key} for {Amount} {Currency}: {Outcome}.", idempotencyKey, request.Amount, request.Currency, result.Outcome);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ChargeResult> DecideAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        var settings = options.CurrentValue;

        if (request.Amount == settings.AlwaysTimeoutAmount || Random.Shared.NextDouble() < settings.TimeoutRate)
        {
            await Task.Delay(settings.TimeoutDelayMs, cancellationToken);
            return new ChargeResult(ChargeOutcome.Timeout);
        }

        var delay = Random.Shared.Next(settings.MinDelayMs, Math.Max(settings.MinDelayMs, settings.MaxDelayMs) + 1);
        await Task.Delay(delay, cancellationToken);

        if (request.Amount == settings.AlwaysUnavailableAmount || Random.Shared.NextDouble() < settings.TransientErrorRate)
        {
            return new ChargeResult(ChargeOutcome.Unavailable);
        }

        if (request.Amount == settings.AlwaysDeclineAmount)
        {
            return Declined("insufficient_funds");
        }

        if (Random.Shared.NextDouble() < settings.DeclineRate)
        {
            return Declined("do_not_honor");
        }

        var transactionId = $"txn_{Guid.NewGuid():N}";
        return new ChargeResult(ChargeOutcome.Approved, new ChargeResponse("approved", transactionId, null));
    }

    private static ChargeResult Declined(string reason) =>
        new(ChargeOutcome.Declined, new ChargeResponse("declined", null, reason));

    private static string Fingerprint(ChargeRequest request) =>
        string.Create(CultureInfo.InvariantCulture, $"{request.Amount:0.00}|{request.Currency.ToUpperInvariant()}|{request.Reference}");

    private sealed record StoredCharge(string Fingerprint, ChargeResponse Response);
}
