using System.Collections.Concurrent;
using PaymentSystem.Application.Abstractions.Providers;

namespace PaymentSystem.Api.IntegrationTests.Infrastructure;

public sealed class StubPaymentProviderClient : IPaymentProviderClient
{
    public const decimal DeclineAmount = 666m;
    public const decimal UnavailableAmount = 503m;
    public const decimal SlowAmount = 777m;

    private readonly ConcurrentDictionary<Guid, int> _calls = new();

    public int CallsFor(Guid paymentId) => _calls.GetValueOrDefault(paymentId);

    public int TotalCalls => _calls.Values.Sum();

    public async Task<ProviderChargeResult> ChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken)
    {
        _calls.AddOrUpdate(request.PaymentId, 1, (_, count) => count + 1);

        if (request.Amount == SlowAmount)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        }

        return request.Amount switch
        {
            DeclineAmount => ProviderChargeResult.Declined("insufficient_funds"),
            UnavailableAmount => ProviderChargeResult.Unavailable("Provider responded with HTTP 503."),
            _ => ProviderChargeResult.Approved($"txn_{request.PaymentId:N}")
        };
    }
}
