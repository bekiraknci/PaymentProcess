using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Messaging;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Abstractions.Providers;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Payments.Processing;
using PaymentSystem.Application.Payments.Retry;
using PaymentSystem.Application.Reports;
using PaymentSystem.Infrastructure.Persistence;
using PaymentSystem.Infrastructure.Persistence.Interceptors;
using PaymentSystem.Infrastructure.Persistence.Queries;
using PaymentSystem.Infrastructure.Persistence.Repositories;
using PaymentSystem.Infrastructure.Processing;
using PaymentSystem.Infrastructure.Providers;
using PaymentSystem.Infrastructure.Security;
using Polly;

namespace PaymentSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        services.AddPersistence(configuration);
        services.AddSecurity(configuration);
        services.AddPaymentProcessing(configuration);
        services.AddPaymentProviderClient(configuration);

        return services;
    }

    private static void AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditableEntityInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("PaymentSystem")
                ?? throw new InvalidOperationException("Connection string 'PaymentSystem' is not configured.");

            options
                .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure())
                .AddInterceptors(sp.GetRequiredService<AuditableEntityInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentProviderRepository, PaymentProviderRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IPaymentQueries, PaymentQueries>();
        services.AddScoped<IReportingQueries, ReportingQueries>();
    }

    private static void AddSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
    }

    private static void AddPaymentProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaymentProcessingOptions>()
            .Bind(configuration.GetSection(PaymentProcessingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<RetryJobOptions>()
            .Bind(configuration.GetSection(RetryJobOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<IPaymentQueue, ChannelPaymentQueue>();
        services.AddHostedService<PaymentProcessingWorker>();
        services.AddHostedService<FailedPaymentRetryJob>();
    }

    private static void AddPaymentProviderClient(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaymentProviderOptions>()
            .Bind(configuration.GetSection(PaymentProviderOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                o => o.AttemptTimeoutSeconds <= o.TotalTimeoutSeconds,
                "PaymentProvider:AttemptTimeoutSeconds must not exceed PaymentProvider:TotalTimeoutSeconds.")
            .ValidateOnStart();

        services.AddOptions<PaymentProcessingOptions>()
            .Validate<IOptions<PaymentProviderOptions>>(
                (processing, provider) => processing.LeaseSeconds > provider.Value.TotalTimeoutSeconds,
                "PaymentProcessing:LeaseSeconds must be greater than PaymentProvider:TotalTimeoutSeconds, otherwise a payment can be released and charged again while its provider call is still running.")
            .ValidateOnStart();

        services
            .AddHttpClient<IPaymentProviderClient, FakePaymentProviderClient>((sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<PaymentProviderOptions>>().Value;
                client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler()
            .Configure((options, sp) =>
            {
                var providerOptions = sp.GetRequiredService<IOptions<PaymentProviderOptions>>().Value;
                var attemptTimeout = TimeSpan.FromSeconds(providerOptions.AttemptTimeoutSeconds);

                options.AttemptTimeout.Timeout = attemptTimeout;
                options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(providerOptions.TotalTimeoutSeconds);

                options.Retry.MaxRetryAttempts = Math.Max(1, providerOptions.MaxRetryAttempts);
                if (providerOptions.MaxRetryAttempts == 0)
                {
                    options.Retry.ShouldHandle = _ => PredicateResult.False();
                }

                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                options.Retry.Delay = TimeSpan.FromMilliseconds(500);

                options.CircuitBreaker.SamplingDuration = attemptTimeout * 2;
                options.CircuitBreaker.MinimumThroughput = providerOptions.CircuitBreakerMinimumThroughput;
                options.CircuitBreaker.FailureRatio = 0.5;
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(providerOptions.CircuitBreakerBreakSeconds);
            });
    }
}
