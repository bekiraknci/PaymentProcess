using PaymentSystem.Domain.Common;

namespace PaymentSystem.Domain.Users;

public sealed class RolePermission
{
    private RolePermission()
    {
        PermissionName = null!;
    }

    private RolePermission(Role role, string permissionName)
    {
        Role = role;
        PermissionName = permissionName;
    }

    public Role Role { get; private set; }
    public string PermissionName { get; private set; }

    public static RolePermission Grant(Role role, Permission permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        if (!Enum.IsDefined(role))
        {
            throw new DomainException("role_permission.invalid_role", "Role is not valid.");
        }

        return new RolePermission(role, permission.Name);
    }
}
