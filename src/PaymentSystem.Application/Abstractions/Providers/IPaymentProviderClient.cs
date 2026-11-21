namespace PaymentSystem.Application.Abstractions.Providers;

public sealed record ProviderChargeRequest(Guid PaymentId, decimal Amount, string Currency);

public enum ProviderChargeStatus
{
    Approved,
    Declined,
    Unavailable
}

public sealed record ProviderChargeResult(ProviderChargeStatus Status, string? TransactionId, string? Reason)
{
    public static ProviderChargeResult Approved(string transactionId) => new(ProviderChargeStatus.Approved, transactionId, null);

    public static ProviderChargeResult Declined(string reason) => new(ProviderChargeStatus.Declined, null, reason);

    public static ProviderChargeResult Unavailable(string reason) => new(ProviderChargeStatus.Unavailable, null, reason);
}

public interface IPaymentProviderClient
{
    Task<ProviderChargeResult> ChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken);
}
