using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Infrastructure.Security;

namespace PaymentSystem.Api.Security;

internal sealed class CurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid Id =>
        Guid.TryParse(Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : throw new InvalidOperationException("Current request has no authenticated user.");

    public string? Email => Principal?.FindFirstValue(JwtRegisteredClaimNames.Email);

    public string? Role => Principal?.FindFirstValue(JwtTokenService.RoleClaimType);

    public IReadOnlyList<string> Permissions =>
        Principal?.FindAll(Application.Authorization.Permissions.ClaimType).Select(c => c.Value).Distinct().Order(StringComparer.Ordinal).ToList() ?? [];

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(Application.Authorization.Permissions.ClaimType, permission) == true;
}
