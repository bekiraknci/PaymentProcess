using Microsoft.AspNetCore.Identity;
using PaymentSystem.Domain.Users;
using IAppPasswordHasher = PaymentSystem.Application.Abstractions.Security.IPasswordHasher;

namespace PaymentSystem.Infrastructure.Security;

internal sealed class PasswordHasher : IAppPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    public string Hash(string password) => _inner.HashPassword(null!, password);

    public bool Verify(string passwordHash, string providedPassword) =>
        _inner.VerifyHashedPassword(null!, passwordHash, providedPassword) != PasswordVerificationResult.Failed;
}
