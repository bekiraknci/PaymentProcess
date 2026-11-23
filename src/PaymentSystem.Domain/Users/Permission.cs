using PaymentSystem.Domain.Common;

namespace PaymentSystem.Domain.Users;

public sealed class Permission
{
    public const int NameMaxLength = 64;
    public const int DescriptionMaxLength = 256;

    private Permission()
    {
        Name = null!;
        Description = null!;
    }

    private Permission(string name, string description)
    {
        Name = name;
        Description = description;
    }

    public string Name { get; private set; }
    public string Description { get; private set; }

    public static Permission Create(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > NameMaxLength)
        {
            throw new DomainException("permission.invalid_name", $"Permission name is required and can be at most {NameMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(description) || description.Length > DescriptionMaxLength)
        {
            throw new DomainException("permission.invalid_description", $"Permission description is required and can be at most {DescriptionMaxLength} characters.");
        }

        return new Permission(name.Trim(), description.Trim());
    }
}
