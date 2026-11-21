using PaymentSystem.Domain.Common;

namespace PaymentSystem.Domain.Payments;

public sealed record Money
{
    public const int MaxDecimalPlaces = 2;
    public const decimal MaxAmount = 9_999_999_999_999_999.99m;

    private Money(decimal amount, string currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; }
    public string Currency { get; }

    public static Money Create(decimal amount, string currency)
    {
        if (amount <= 0)
        {
            throw new DomainException("money.amount_not_positive", "Amount must be greater than zero.");
        }

        if (amount > MaxAmount)
        {
            throw new DomainException("money.amount_too_large", "Amount exceeds the maximum allowed value.");
        }

        if (decimal.Round(amount, MaxDecimalPlaces) != amount)
        {
            throw new DomainException("money.too_many_decimals", $"Amount can have at most {MaxDecimalPlaces} decimal places.");
        }

        if (!Payments.Currency.IsSupported(currency))
        {
            throw new DomainException("money.unsupported_currency", $"Currency '{currency}' is not supported.");
        }

        return new Money(amount, Payments.Currency.Normalize(currency));
    }

    public override string ToString() => $"{Amount:0.00} {Currency}";
}
