using PaymentSystem.Domain.Providers;

namespace PaymentSystem.Application.Abstractions.Persistence;

public interface IPaymentProviderRepository
{
    Task<PaymentProvider?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentProvider>> ListAsync(CancellationToken cancellationToken = default);

    void Add(PaymentProvider provider);
}
