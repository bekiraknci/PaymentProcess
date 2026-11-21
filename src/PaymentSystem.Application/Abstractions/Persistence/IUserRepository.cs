using PaymentSystem.Domain.Users;

namespace PaymentSystem.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
}
