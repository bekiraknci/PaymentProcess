using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Abstractions.Providers;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Payments.Processing;

public enum ProcessPaymentOutcome
{
    Skipped,
    Completed,
    Failed
}

public sealed class ProcessPaymentHandler(
    IPaymentRepository payments,
    IUnitOfWork unitOfWork,
    IPaymentProviderClient providerClient,
    IOptions<PaymentProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<ProcessPaymentHandler> logger)
{
    public async Task<ProcessPaymentOutcome> HandleAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        var payment = await payments.GetByIdAsync(paymentId, cancellationToken);
        if (payment is null || payment.Status != PaymentStatus.Pending)
        {
            logger.LogDebug("Payment {PaymentId} is not pending; skipping.", paymentId);
            return ProcessPaymentOutcome.Skipped;
        }

        payment.StartProcessing(timeProvider.GetUtcNow(), options.Value.Lease);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            logger.LogInformation("Payment {PaymentId} was claimed by another worker; skipping.", paymentId);
            return ProcessPaymentOutcome.Skipped;
        }

        var result = await providerClient.ChargeAsync(
            new ProviderChargeRequest(payment.Id, payment.Money.Amount, payment.Money.Currency),
            cancellationToken);

        var now = timeProvider.GetUtcNow();
        var outcome = ApplyResult(payment, result, now);

        await unitOfWork.SaveChangesAsync(CancellationToken.None);

        logger.LogInformation(
            "Payment {PaymentId} attempt #{Attempt} finished as {Status}.",
            payment.Id, payment.AttemptCount, payment.Status);

        return outcome;
    }

    private ProcessPaymentOutcome ApplyResult(Payment payment, ProviderChargeResult result, DateTimeOffset now)
    {
        switch (result.Status)
        {
            case ProviderChargeStatus.Approved:
                payment.Complete(result.TransactionId!, now);
                return ProcessPaymentOutcome.Completed;

            case ProviderChargeStatus.Declined:
                payment.Fail($"Declined by provider: {result.Reason}", options.Value.NextAttemptAfter(payment.AttemptCount, now), now);
                return ProcessPaymentOutcome.Failed;

            default:
                payment.Fail($"Provider unavailable: {result.Reason}", options.Value.NextAttemptAfter(payment.AttemptCount, now), now);
                return ProcessPaymentOutcome.Failed;
        }
    }
}
