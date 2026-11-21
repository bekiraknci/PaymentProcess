using FluentValidation;
using PaymentSystem.Application.Abstractions.Messaging;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Common.Results;
using PaymentSystem.Application.Common.Validation;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Payments.CreatePayment;

public sealed class CreatePaymentHandler(
    IValidator<CreatePaymentCommand> validator,
    IPaymentRepository payments,
    IPaymentProviderRepository providers,
    IUnitOfWork unitOfWork,
    IPaymentQueue queue,
    ICurrentUser currentUser,
    TimeProvider timeProvider)
{
    public async Task<Result<CreatePaymentResult>> HandleAsync(CreatePaymentCommand command, CancellationToken cancellationToken)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        var userId = currentUser.Id;
        var idempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : command.IdempotencyKey.Trim();
        var requestHash = command.ComputeRequestHash();

        if (idempotencyKey is not null &&
            await payments.GetByIdempotencyKeyAsync(userId, idempotencyKey, cancellationToken) is { } existing)
        {
            return Replay(existing, requestHash);
        }

        var provider = await providers.GetByIdAsync(command.ProviderId, cancellationToken);
        if (provider is null)
        {
            return Error.NotFound("provider.not_found", $"Payment provider '{command.ProviderId}' was not found.");
        }

        if (!provider.IsActive)
        {
            return FieldError(nameof(command.ProviderId), $"Payment provider '{provider.Code}' is not active.");
        }

        if (!provider.SupportsCurrency(command.Currency))
        {
            return FieldError(
                nameof(command.Currency),
                $"Payment provider '{provider.Code}' does not support {command.Currency.ToUpperInvariant()}. Supported: {string.Join(", ", provider.SupportedCurrencies)}.");
        }

        var payment = Payment.Create(
            userId,
            provider.Id,
            Money.Create(command.Amount, command.Currency),
            idempotencyKey,
            requestHash,
            timeProvider.GetUtcNow());

        payments.Add(payment);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException) when (idempotencyKey is not null)
        {
            var winner = await payments.GetByIdempotencyKeyAsync(userId, idempotencyKey, cancellationToken)
                ?? throw new InvalidOperationException("Idempotency key conflict detected but the original payment was not found.");

            return Replay(winner, requestHash);
        }

        await queue.EnqueueAsync(payment.Id, cancellationToken);

        return new CreatePaymentResult(PaymentResponse.From(payment), Replayed: false);
    }

    private static Result<CreatePaymentResult> Replay(Payment existing, string requestHash) =>
        string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal)
            ? new CreatePaymentResult(PaymentResponse.From(existing), Replayed: true)
            : Error.Conflict(
                "payment.idempotency_key_reused",
                "This Idempotency-Key was already used with a different request body.");

    private static Error FieldError(string field, string message) =>
        Error.Validation(new Dictionary<string, string[]>
        {
            [char.ToLowerInvariant(field[0]) + field[1..]] = [message]
        });
}
