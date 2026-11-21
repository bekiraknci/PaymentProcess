using PaymentSystem.Application.Common.Paging;
using PaymentSystem.Application.Payments;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Abstractions.Persistence;

public interface IPaymentQueries
{
    Task<PaymentResponse?> GetByIdAsync(Guid id, Guid? ownerId, CancellationToken cancellationToken = default);

    Task<PagedResult<PaymentResponse>> ListAsync(
        Guid? ownerId,
        PaymentStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PaymentHistoryResponse>?> GetHistoryAsync(Guid id, Guid? ownerId, CancellationToken cancellationToken = default);
}
