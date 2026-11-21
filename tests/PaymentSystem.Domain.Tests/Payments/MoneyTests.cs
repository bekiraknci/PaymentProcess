using FluentAssertions;
using PaymentSystem.Domain.Common;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Domain.Tests.Payments;

public sealed class MoneyTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Create_rejects_non_positive_amount(decimal amount)
    {
        var act = () => Money.Create(amount, "USD");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("money.amount_not_positive");
    }

    [Fact]
    public void Create_rejects_more_than_two_decimal_places()
    {
        var act = () => Money.Create(10.555m, "USD");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("money.too_many_decimals");
    }

    [Theory]
    [InlineData("XYZ")]
    [InlineData("US")]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_unsupported_currency(string currency)
    {
        var act = () => Money.Create(10m, currency);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("money.unsupported_currency");
    }

    [Fact]
    public void Create_normalizes_currency_to_upper_case()
    {
        var money = Money.Create(125.50m, " try ");

        money.Amount.Should().Be(125.50m);
        money.Currency.Should().Be("TRY");
    }

    [Fact]
    public void Money_has_value_equality()
    {
        Money.Create(10m, "EUR").Should().Be(Money.Create(10.00m, "eur"));
        Money.Create(10m, "EUR").Should().NotBe(Money.Create(10m, "USD"));
    }
}
