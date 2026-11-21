using PaymentSystem.Domain.Common;

namespace PaymentSystem.Domain.Users;

public sealed class User : AggregateRoot
{
    public const int EmailMaxLength = 256;

    private User()
    {
        Email = null!;
        PasswordHash = null!;
    }

    private User(Guid id, string email, string passwordHash, Role role) : base(id)
    {
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
    }

    public string Email { get; private set; }
    public string PasswordHash { get; private set; }
    public Role Role { get; private set; }

    public bool IsAdmin => Role == Role.Admin;

    public static User Create(string email, string passwordHash, Role role, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("user.password_hash_required", "Password hash is required.");
        }

        if (!Enum.IsDefined(role))
        {
            throw new DomainException("user.invalid_role", "Role is not valid.");
        }

        return new User(Guid.CreateVersion7(now), NormalizeEmail(email), passwordHash, role);
    }

    public void ChangePasswordHash(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new DomainException("user.password_hash_required", "Password hash is required.");
        }

        PasswordHash = passwordHash;
    }

    public static string NormalizeEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@') || email.Length > EmailMaxLength)
        {
            throw new DomainException("user.invalid_email", "Email is not valid.");
        }

        return email.Trim().ToLowerInvariant();
    }
}
