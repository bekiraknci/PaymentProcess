extern alias fake;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentSystem.Api.IntegrationTests.Infrastructure;

public sealed class FakeProviderService : WebApplicationFactory<fake::Program>
{
    public const int FlakyFailuresPerKey = 3;

    public FakeProviderService()
    {
        UseKestrel(0);
    }

    public Uri Address { get; private set; } = null!;

    public void Start()
    {
        StartServer();
        var addresses = Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var address = addresses.First()
            .Replace("[::]", "127.0.0.1", StringComparison.Ordinal)
            .Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal);
        Address = new Uri(address);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseUrls("http://127.0.0.1:0");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Scenarios:MinDelayMs"] = "0",
            ["Scenarios:MaxDelayMs"] = "10",
            ["Scenarios:DeclineRate"] = "0",
            ["Scenarios:TransientErrorRate"] = "0",
            ["Scenarios:TimeoutRate"] = "0",
            ["Scenarios:TimeoutDelayMs"] = "5000",
            ["Scenarios:FlakyFailuresPerKey"] = FlakyFailuresPerKey.ToString(),
            ["Logging:LogLevel:Default"] = "Warning"
        }));
    }
}

public sealed class EndToEndFactory : PaymentApiFactory
{
    public FakeProviderService ProviderService { get; } = new();

    protected override bool UseStubProvider => false;

    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["PaymentProvider:BaseUrl"] = ProviderService.Address.ToString();
            settings["PaymentProvider:AttemptTimeoutSeconds"] = "1";
            settings["PaymentProvider:TotalTimeoutSeconds"] = "8";
            settings["PaymentProvider:MaxRetryAttempts"] = "2";
            settings["PaymentProvider:CircuitBreakerMinimumThroughput"] = "100000";
            settings["PaymentProcessing:RetryBaseDelaySeconds"] = "1";
            settings["PaymentProcessing:MaxAutomaticAttempts"] = "3";
            settings["RetryJob:Enabled"] = "true";
            settings["RetryJob:IntervalSeconds"] = "1";
            return settings;
        }
    }

    public override async Task InitializeAsync()
    {
        ProviderService.Start();
        await base.InitializeAsync();
    }

    protected override async Task OnDisposedAsync() => await ProviderService.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class EndToEndCollection : ICollectionFixture<EndToEndFactory>
{
    public const string Name = "e2e";
}
