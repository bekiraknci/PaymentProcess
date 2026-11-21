using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Common.Results;

namespace PaymentSystem.Application.Payments.GetPayment;

public sealed class GetPaymentByIdHandler(IPaymentQueries queries, ICurrentUser currentUser)
{
    public async Task<Result<PaymentResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var payment = await queries.GetByIdAsync(id, currentUser.OwnerScope(), cancellationToken);
        return payment is null ? PaymentErrors.NotFound(id) : Result<PaymentResponse>.Success(payment);
    }
}

public sealed class GetPaymentHistoryHandler(IPaymentQueries queries, ICurrentUser currentUser)
{
    public async Task<Result<IReadOnlyList<PaymentHistoryResponse>>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var history = await queries.GetHistoryAsync(id, currentUser.OwnerScope(), cancellationToken);
        return history is null
            ? PaymentErrors.NotFound(id)
            : Result<IReadOnlyList<PaymentHistoryResponse>>.Success(history);
    }
}
