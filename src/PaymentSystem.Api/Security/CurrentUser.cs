using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Authorization;
using PaymentSystem.Domain.Users;

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

    public bool IsAdmin => Principal?.IsInRole(nameof(Role.Admin)) == true;

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(Permissions.ClaimType, permission) == true;
}
