using FluentAssertions;
using PaymentSystem.Domain.Common;
using PaymentSystem.Domain.Users;

namespace PaymentSystem.Domain.Tests.Users;

public sealed class PermissionTests
{
    [Fact]
    public void Create_trims_name_and_description()
    {
        var permission = Permission.Create(" Payments.View ", " View payments. ");

        permission.Name.Should().Be("Payments.View");
        permission.Description.Should().Be("View payments.");
    }

    [Theory]
    [InlineData("", "Description")]
    [InlineData("   ", "Description")]
    public void Create_rejects_a_missing_name(string name, string description)
    {
        var act = () => Permission.Create(name, description);

        act.Should().Throw<DomainException>().Which.Code.Should().Be("permission.invalid_name");
    }

    [Fact]
    public void Create_rejects_a_name_that_is_too_long()
    {
        var act = () => Permission.Create(new string('a', Permission.NameMaxLength + 1), "Description");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("permission.invalid_name");
    }

    [Fact]
    public void Create_rejects_a_missing_description()
    {
        var act = () => Permission.Create("Payments.View", " ");

        act.Should().Throw<DomainException>().Which.Code.Should().Be("permission.invalid_description");
    }

    [Fact]
    public void Grant_links_a_role_to_the_permission_name()
    {
        var grant = RolePermission.Grant(Role.User, Permission.Create("Payments.View", "View payments."));

        grant.Role.Should().Be(Role.User);
        grant.PermissionName.Should().Be("Payments.View");
    }

    [Fact]
    public void Grant_rejects_an_unknown_role()
    {
        var act = () => RolePermission.Grant((Role)99, Permission.Create("Payments.View", "View payments."));

        act.Should().Throw<DomainException>().Which.Code.Should().Be("role_permission.invalid_role");
    }
}
