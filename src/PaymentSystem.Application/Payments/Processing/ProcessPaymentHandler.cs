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

        var result = await ChargeAsync(payment, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var outcome = ApplyResult(payment, result, now);

        try
        {
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
        catch (ConcurrencyConflictException)
        {
            logger.LogWarning(
                "Payment {PaymentId} changed while attempt #{Attempt} was in flight (lease expired?); the result was discarded and the payment will be reconciled by its next attempt.",
                paymentId, payment.AttemptCount);
            return ProcessPaymentOutcome.Skipped;
        }

        logger.LogInformation(
            "Payment {PaymentId} attempt #{Attempt} finished as {Status}.",
            payment.Id, payment.AttemptCount, payment.Status);

        return outcome;
    }

    private async Task<ProviderChargeResult> ChargeAsync(Payment payment, CancellationToken cancellationToken)
    {
        try
        {
            return await providerClient.ChargeAsync(
                new ProviderChargeRequest(payment.Id, payment.Money.Amount, payment.Money.Currency),
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Provider call for payment {PaymentId} failed unexpectedly.", payment.Id);
            return ProviderChargeResult.Unavailable("Unexpected error while calling the provider.");
        }
    }

    private static DateTimeOffset? NotBefore(DateTimeOffset? scheduled, DateTimeOffset now, TimeSpan? retryAfter) =>
        scheduled is { } next && retryAfter is { } wait && now + wait > next ? now + wait : scheduled;

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
                payment.Fail($"Provider unavailable: {result.Reason}", NotBefore(options.Value.NextAttemptAfter(payment.AttemptCount, now), now, result.RetryAfter), now);
                return ProcessPaymentOutcome.Failed;
        }
    }
}
