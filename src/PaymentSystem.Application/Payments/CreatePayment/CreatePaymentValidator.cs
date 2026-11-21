using FluentValidation;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Payments.CreatePayment;

public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentValidator()
    {
        RuleFor(x => x.ProviderId)
            .NotEmpty();

        RuleFor(x => x.Amount)
            .GreaterThan(0)
            .LessThanOrEqualTo(Money.MaxAmount)
            .PrecisionScale(18, Money.MaxDecimalPlaces, ignoreTrailingZeros: true);

        RuleFor(x => x.Currency)
            .NotEmpty()
            .Length(3)
            .Must(Currency.IsSupported)
            .WithMessage($"Currency must be one of: {string.Join(", ", Currency.Supported.Order())}.");

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(Payment.IdempotencyKeyMaxLength)
            .Matches("^[\\x21-\\x7E]+$")
            .WithMessage("Idempotency-Key may contain only printable ASCII characters without spaces.")
            .When(x => !string.IsNullOrEmpty(x.IdempotencyKey));
    }
}
