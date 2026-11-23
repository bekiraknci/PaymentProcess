using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Authorization;

namespace PaymentSystem.Api.Security;

internal sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (await base.GetPolicyAsync(policyName) is { } policy)
        {
            return policy;
        }

        if (string.IsNullOrWhiteSpace(policyName))
        {
            return null;
        }

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireClaim(Permissions.ClaimType, policyName)
            .Build();
    }
}
