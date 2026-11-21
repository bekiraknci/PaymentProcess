using PaymentSystem.Application.Abstractions.Messaging;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Common.Results;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Payments.RetryPayment;

public sealed class RetryPaymentHandler(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IPaymentQueue queue,
    TimeProvider timeProvider)
{
    public async Task<Result<PaymentResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var payment = await payments.GetByIdAsync(id, cancellationToken);
        if (payment is null)
        {
            return PaymentErrors.NotFound(id);
        }

        if (payment.Status != PaymentStatus.Failed)
        {
            return Error.Conflict(
                "payment.not_retryable",
                $"Only failed payments can be retried. Current status: {payment.Status}.");
        }

        payment.RequeueForRetry(timeProvider.GetUtcNow(), "Manual retry requested by an administrator.");

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Error.Conflict("payment.concurrent_update", "The payment was modified concurrently. Please retry.");
        }

        await queue.EnqueueAsync(payment.Id, cancellationToken);
        return PaymentResponse.From(payment);
    }
}
