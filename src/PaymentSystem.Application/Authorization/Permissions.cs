namespace PaymentSystem.Application.Authorization;

public static class Permissions
{
    public const string ClaimType = "permission";

    public const string PaymentsCreate = "Payments.Create";
    public const string PaymentsView = "Payments.View";
    public const string PaymentsProcess = "Payments.Process";
    public const string ProvidersManage = "Providers.Manage";

    public static IReadOnlyList<string> All { get; } =
        [PaymentsCreate, PaymentsView, PaymentsProcess, ProvidersManage];
}
