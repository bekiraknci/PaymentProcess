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

    private readonly bool _dropDatabaseOnDispose;
    private readonly IReadOnlyDictionary<string, string?> _overrides;

    public PaymentApiFactory()
        : this(null, dropDatabaseOnDispose: true, overrides: null)
    {
    }

    protected PaymentApiFactory(string? databaseName, bool dropDatabaseOnDispose, IReadOnlyDictionary<string, string?>? overrides)
    {
        DatabaseName = databaseName ?? NewDatabaseName();
        _dropDatabaseOnDispose = dropDatabaseOnDispose;
        _overrides = overrides ?? new Dictionary<string, string?>();
    }

    public string DatabaseName { get; }

    public StubPaymentProviderClient Provider { get; } = new();

    public string ConnectionString => TestSqlServer.ConnectionString(DatabaseName);

    public string SigningKey => Services.GetRequiredService<IConfiguration>()["Jwt:SigningKey"]!;

    public static string NewDatabaseName() => $"PaymentSystem_IT_{Guid.NewGuid():N}";

    protected virtual bool UseStubProvider => true;

    protected virtual IDictionary<string, string?> Settings
    {
        get
        {
            var settings = new Dictionary<string, string?>
            {
                ["ConnectionStrings:PaymentSystem"] = ConnectionString,
                ["Database:MigrateOnStartup"] = "true",
                ["PaymentProcessing:WorkerCount"] = "4",
                ["PaymentProcessing:PollIntervalSeconds"] = "1",
                ["PaymentProcessing:LeaseSeconds"] = "30",
                ["RetryJob:Enabled"] = "false",
                ["RateLimiting:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning"
            };

            foreach (var (key, value) in _overrides)
            {
                settings[key] = value;
            }

            return settings;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(Settings));
        builder.ConfigureServices(services =>
        {
            if (UseStubProvider)
            {
                services.RemoveAll<IPaymentProviderClient>();
                services.AddSingleton<IPaymentProviderClient>(Provider);
            }

            ConfigureTestServices(services);
        });
    }

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    public virtual async Task InitializeAsync()
    {
        _ = Server;

        var database = await WithDbAsync(db => Task.FromResult(db.Database.GetDbConnection().Database));
        if (!string.Equals(database, DatabaseName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Integration tests must run against their own database '{DatabaseName}', but the app is configured for '{database}'. Aborting to protect real data.");
        }
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await OnDisposedAsync();

        if (_dropDatabaseOnDispose)
        {
            await DropDatabaseAsync(DatabaseName);
        }
    }

    protected virtual Task OnDisposedAsync() => Task.CompletedTask;

    public static async Task DropDatabaseAsync(string databaseName)
    {
        if (!databaseName.StartsWith("PaymentSystem_IT_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Refusing to drop non-test database '{databaseName}'.");
        }

        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(TestSqlServer.ConnectionString("master"));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF DB_ID(N'{databaseName}') IS NOT NULL BEGIN ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END";
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

    public async Task ExecuteSqlAsync(string sql)
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}

public sealed class RateLimitedApiFactory() : PaymentApiFactory(null, dropDatabaseOnDispose: true, new Dictionary<string, string?>
{
    ["RateLimiting:Enabled"] = "true",
    ["RateLimiting:LoginPermitLimit"] = "5",
    ["RateLimiting:LoginWindowSeconds"] = "60",
    ["RateLimiting:PaymentCreateBurst"] = "5",
    ["RateLimiting:PaymentCreatePerSecond"] = "1"
});

public sealed class ConfiguredApiFactory(string? databaseName, bool dropDatabaseOnDispose, IReadOnlyDictionary<string, string?>? overrides)
    : PaymentApiFactory(databaseName, dropDatabaseOnDispose, overrides);

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<PaymentApiFactory>
{
    public const string Name = "api";
}
