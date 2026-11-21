using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaymentSystem.Application.Abstractions.Providers;
using PaymentSystem.Infrastructure.Persistence;

namespace PaymentSystem.Api.IntegrationTests.Infrastructure;

public class PaymentApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string FakePayId = "0196a000-0000-7000-8000-000000000001";
    public const string LegacyPayId = "0196a000-0000-7000-8000-000000000002";

    private readonly string _databaseName = $"PaymentSystem_IT_{Guid.NewGuid():N}";

    public StubPaymentProviderClient Provider { get; } = new();

    public string ConnectionString =>
        TestSqlServer.ConnectionString(_databaseName);

    public string SigningKey => Services.GetRequiredService<IConfiguration>()["Jwt:SigningKey"]!;

    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["ConnectionStrings:PaymentSystem"] = ConnectionString,
        ["Database:MigrateOnStartup"] = "true",
        ["PaymentProcessing:WorkerCount"] = "4",
        ["PaymentProcessing:PollIntervalSeconds"] = "1",
        ["PaymentProcessing:LeaseSeconds"] = "30",
        ["RateLimiting:Enabled"] = "false",
        ["Logging:LogLevel:Default"] = "Warning"
    };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IPaymentProviderClient>();
            services.AddSingleton<IPaymentProviderClient>(Provider);
        });
    }

    public async Task InitializeAsync()
    {
        _ = Server;

        var database = await WithDbAsync(db => Task.FromResult(db.Database.GetDbConnection().Database));
        if (!string.Equals(database, _databaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Integration tests must run against their own database '{_databaseName}', but the app is configured for '{database}'. Aborting to protect real data.");
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();

        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(TestSqlServer.ConnectionString("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{_databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]; END";
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<SqlConnection> OpenConnectionAsync()
    {
        var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }
}

public sealed class RateLimitedApiFactory : PaymentApiFactory
{
    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["RateLimiting:Enabled"] = "true";
            settings["RateLimiting:LoginPermitLimit"] = "5";
            settings["RateLimiting:LoginWindowSeconds"] = "60";
            settings["RateLimiting:PaymentCreateBurst"] = "5";
            settings["RateLimiting:PaymentCreatePerSecond"] = "1";
            return settings;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PaymentApiFactory>
{
    public const string Name = "api";
}
