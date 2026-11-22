using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Payments.Processing;
using PaymentSystem.Infrastructure;
using PaymentSystem.Infrastructure.Providers;

namespace PaymentSystem.Api.IntegrationTests.Providers;

public sealed class OptionsValidationTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_configuration_is_valid()
    {
        using var provider = Build([]);

        provider.GetRequiredService<IOptions<PaymentProcessingOptions>>().Value.LeaseSeconds.Should().Be(120);
        provider.GetRequiredService<IOptions<PaymentProviderOptions>>().Value.TotalTimeoutSeconds.Should().Be(20);
    }

    [Fact]
    public void Lease_shorter_than_the_provider_timeout_is_rejected()
    {
        using var provider = Build(new()
        {
            ["PaymentProcessing:LeaseSeconds"] = "15",
            ["PaymentProvider:TotalTimeoutSeconds"] = "20"
        });

        var act = () => provider.GetRequiredService<IOptions<PaymentProcessingOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*LeaseSeconds*TotalTimeoutSeconds*");
    }

    [Fact]
    public void Attempt_timeout_longer_than_total_timeout_is_rejected()
    {
        using var provider = Build(new()
        {
            ["PaymentProvider:AttemptTimeoutSeconds"] = "30",
            ["PaymentProvider:TotalTimeoutSeconds"] = "20"
        });

        var act = () => provider.GetRequiredService<IOptions<PaymentProviderOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*AttemptTimeoutSeconds*");
    }
}
