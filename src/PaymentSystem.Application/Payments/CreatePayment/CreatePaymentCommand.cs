using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PaymentSystem.Application.Payments.CreatePayment;

public sealed record CreatePaymentCommand(Guid ProviderId, decimal Amount, string Currency, string? IdempotencyKey)
{
    public string ComputeRequestHash()
    {
        var canonical = string.Join(
            '|',
            ProviderId.ToString("N"),
            Amount.ToString("0.00", CultureInfo.InvariantCulture),
            Currency.Trim().ToUpperInvariant());

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

public sealed record CreatePaymentResult(PaymentResponse Payment, bool Replayed);
