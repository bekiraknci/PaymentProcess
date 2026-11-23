using PaymentSystem.Domain.Users;

namespace PaymentSystem.Application.Abstractions.Persistence;

public interface IPermissionRepository
{
    Task<IReadOnlyList<string>> GetForRoleAsync(Role role, CancellationToken cancellationToken = default);
}
