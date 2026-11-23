namespace PaymentSystem.Application.Authorization;

public static class Permissions
{
    public const string ClaimType = "permission";

    public const string PaymentsCreate = "Payments.Create";
    public const string PaymentsView = "Payments.View";
    public const string PaymentsViewAll = "Payments.ViewAll";
    public const string PaymentsProcess = "Payments.Process";
    public const string ProvidersManage = "Providers.Manage";
    public const string ReportsView = "Reports.View";

    public static IReadOnlyList<string> All { get; } =
        [PaymentsCreate, PaymentsView, PaymentsViewAll, PaymentsProcess, ProvidersManage, ReportsView];
}
