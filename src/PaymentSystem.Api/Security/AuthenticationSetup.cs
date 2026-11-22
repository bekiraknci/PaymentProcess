using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Authorization;
using PaymentSystem.Domain.Users;
using PaymentSystem.Infrastructure.Security;

namespace PaymentSystem.Api.Security;

internal static class AuthenticationSetup
{
    public const string AdminOnlyPolicy = "AdminOnly";

    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtTokenService.CreateSigningKey(jwt.SigningKey),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Email,
                    RoleClaimType = JwtTokenService.RoleClaimType
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        context.Token = ExtractToken(context.Request.Headers.Authorization.ToString());
                        return Task.CompletedTask;
                    }
                };
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminOnlyPolicy, policy => policy.RequireRole(nameof(Role.Admin)))
            .AddPermissionPolicies();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();

        return services;
    }

    private static string? ExtractToken(string authorization)
    {
        const string prefix = "Bearer ";
        var value = authorization.Trim();

        while (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = value[prefix.Length..].TrimStart();
        }

        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static AuthorizationBuilder AddPermissionPolicies(this AuthorizationBuilder builder)
    {
        foreach (var permission in Permissions.All)
        {
            builder.AddPolicy(permission, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(Permissions.ClaimType, permission));
        }

        return builder;
    }
}
