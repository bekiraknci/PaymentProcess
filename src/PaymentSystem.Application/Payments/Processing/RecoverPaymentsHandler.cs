using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Persistence;

namespace PaymentSystem.Application.Payments.Processing;

public sealed class RecoverPaymentsHandler(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IOptions<PaymentProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<RecoverPaymentsHandler> logger)
{
    public async Task<IReadOnlyList<Guid>> HandleAsync(CancellationToken cancellationToken)
    {
        var batchSize = options.Value.RecoveryBatchSize;
        var now = timeProvider.GetUtcNow();

        var stuck = await payments.GetExpiredProcessingAsync(now, batchSize, cancellationToken);
        if (stuck.Count > 0)
        {
            foreach (var payment in stuck)
            {
                payment.ReleaseExpiredLease(now);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
                logger.LogWarning("Released {Count} payment(s) with expired processing leases.", stuck.Count);
            }
            catch (ConcurrencyConflictException)
            {
                logger.LogInformation("Expired leases were changed concurrently; they will be re-evaluated on the next pass.");
            }
        }

        return await payments.GetPendingIdsAsync(batchSize, cancellationToken);
    }
}
