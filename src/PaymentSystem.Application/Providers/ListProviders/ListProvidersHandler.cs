using PaymentSystem.Application.Abstractions.Persistence;

namespace PaymentSystem.Application.Providers.ListProviders;

public sealed class ListProvidersHandler(IPaymentProviderRepository providers)
{
    public async Task<IReadOnlyList<ProviderResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var items = await providers.ListAsync(cancellationToken);
        return items.Select(ProviderResponse.From).ToList();
    }
}
