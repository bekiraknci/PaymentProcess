using Microsoft.EntityFrameworkCore;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Domain.Users;

namespace PaymentSystem.Infrastructure.Persistence.Repositories;

internal sealed class PermissionRepository(AppDbContext db) : IPermissionRepository
{
    public async Task<IReadOnlyList<string>> GetForRoleAsync(Role role, CancellationToken cancellationToken = default) =>
        await db.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.Role == role)
            .OrderBy(rp => rp.PermissionName)
            .Select(rp => rp.PermissionName)
            .ToListAsync(cancellationToken);
}
