namespace PaymentSystem.Application.Abstractions.Security;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid Id { get; }

    string? Email { get; }

    bool IsAdmin { get; }

    bool HasPermission(string permission);
}
