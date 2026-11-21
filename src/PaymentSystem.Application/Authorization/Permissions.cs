using PaymentSystem.Domain.Users;

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

public static class RolePermissions
{
    private static readonly IReadOnlyDictionary<Role, IReadOnlyList<string>> Map =
        new Dictionary<Role, IReadOnlyList<string>>
        {
            [Role.Admin] =
            [
                Permissions.PaymentsCreate,
                Permissions.PaymentsView,
                Permissions.PaymentsProcess,
                Permissions.ProvidersManage
            ],
            [Role.User] =
            [
                Permissions.PaymentsCreate,
                Permissions.PaymentsView
            ]
        };

    public static IReadOnlyList<string> For(Role role) =>
        Map.TryGetValue(role, out var permissions) ? permissions : [];
}
