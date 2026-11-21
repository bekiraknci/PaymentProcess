using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace PaymentSystem.Api.RateLimiting;

public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; init; } = true;

    [Range(1, 10_000)]
    public int LoginPermitLimit { get; init; } = 10;

    [Range(1, 3600)]
    public int LoginWindowSeconds { get; init; } = 60;

    [Range(1, 100_000)]
    public int PaymentCreateBurst { get; init; } = 100;

    [Range(1, 100_000)]
    public int PaymentCreatePerSecond { get; init; } = 50;

    [Range(1, 1_000_000)]
    public int GlobalBurst { get; init; } = 500;

    [Range(1, 1_000_000)]
    public int GlobalPerSecond { get; init; } = 200;
}

public static class RateLimitPolicies
{
    public const string Login = "login";
    public const string PaymentCreate = "payment-create";
}

internal static class RateLimitingSetup
{
    private const string DisabledPartition = "disabled";

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = WriteProblemAsync;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var options = Settings(context);
                return options.Enabled
                    ? RateLimitPartition.GetTokenBucketLimiter(ClientKey(context), _ => TokenBucket(options.GlobalBurst, options.GlobalPerSecond))
                    : RateLimitPartition.GetNoLimiter(DisabledPartition);
            });

            limiter.AddPolicy(RateLimitPolicies.Login, context =>
            {
                var options = Settings(context);
                return options.Enabled
                    ? RateLimitPartition.GetFixedWindowLimiter(IpKey(context), _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.LoginPermitLimit,
                        Window = TimeSpan.FromSeconds(options.LoginWindowSeconds),
                        QueueLimit = 0
                    })
                    : RateLimitPartition.GetNoLimiter(DisabledPartition);
            });

            limiter.AddPolicy(RateLimitPolicies.PaymentCreate, context =>
            {
                var options = Settings(context);
                return options.Enabled
                    ? RateLimitPartition.GetTokenBucketLimiter(ClientKey(context), _ => TokenBucket(options.PaymentCreateBurst, options.PaymentCreatePerSecond))
                    : RateLimitPartition.GetNoLimiter(DisabledPartition);
            });
        });

        return services;
    }

    private static RateLimitingOptions Settings(HttpContext context) =>
        context.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;

    private static TokenBucketRateLimiterOptions TokenBucket(int burst, int perSecond) => new()
    {
        TokenLimit = burst,
        TokensPerPeriod = perSecond,
        ReplenishmentPeriod = TimeSpan.FromSeconds(1),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    private static string ClientKey(HttpContext context) =>
        context.User.FindFirstValue(JwtRegisteredClaimNames.Sub) is { } userId ? $"user:{userId}" : IpKey(context);

    private static string IpKey(HttpContext context) =>
        $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

    private static async ValueTask WriteProblemAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        var problemDetails = http.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemDetails.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Too many requests",
                Detail = "The request rate limit was exceeded. Retry later.",
                Extensions = { ["code"] = "rate_limited" }
            }
        });
    }
}
