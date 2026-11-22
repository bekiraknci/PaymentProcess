using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Messaging;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Payments.Processing;

namespace PaymentSystem.Application.Payments.Retry;

public sealed class RetryDueFailedPaymentsHandler(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IPaymentQueue queue,
    IOptions<PaymentProcessingOptions> processingOptions,
    IOptions<RetryJobOptions> jobOptions,
    TimeProvider timeProvider,
    ILogger<RetryDueFailedPaymentsHandler> logger)
{
    public async Task<int> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var maxAttempts = processingOptions.Value.MaxAutomaticAttempts;

        var due = await payments.GetDueForAutomaticRetryAsync(now, maxAttempts, jobOptions.Value.BatchSize, cancellationToken);
        if (due.Count == 0)
        {
            return 0;
        }

        foreach (var payment in due)
        {
            payment.RequeueForAutomaticRetry(now, maxAttempts);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            logger.LogInformation("Due payments changed concurrently; they will be re-evaluated on the next tick.");
            return 0;
        }

        foreach (var payment in due)
        {
            await queue.EnqueueAsync(payment.Id, cancellationToken);
        }

        logger.LogInformation("Requeued {Count} failed payment(s) for automatic retry.", due.Count);
        return due.Count;
    }
}
