using FluentAssertions;
using PaymentSystem.Domain.Common;
using PaymentSystem.Domain.Payments;
using PaymentSystem.Domain.Providers;

namespace PaymentSystem.Domain.Tests.Providers;

public sealed class PaymentProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_normalizes_code_and_currencies()
    {
        var provider = PaymentProvider.Create(" fastpay ", "FastPay", null, ["usd", "EUR", "usd"], true, Now);

        provider.Code.Should().Be("FASTPAY");
        provider.SupportedCurrencies.Should().Equal("EUR", "USD");
        provider.SupportsCurrency("eur").Should().BeTrue();
        provider.SupportsCurrency("TRY").Should().BeFalse();
    }

    [Fact]
    public void Create_rejects_unsupported_currency()
    {
        var act = () => PaymentProvider.Create("X", "X", null, ["XYZ"], true, Now);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("provider.unsupported_currency");
    }

    [Fact]
    public void Inactive_provider_cannot_accept_payments()
    {
        var provider = PaymentProvider.Create("X", "X", null, ["USD"], true, Now);
        var money = Money.Create(10m, "USD");

        provider.CanAccept(money).Should().BeTrue();

        provider.Deactivate();
        provider.CanAccept(money).Should().BeFalse();

        provider.Activate();
        provider.CanAccept(money).Should().BeTrue();
    }
}
