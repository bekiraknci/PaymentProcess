using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Api.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace PaymentSystem.Api.IntegrationTests.EndToEnd;

public sealed class RestartRecoveryTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Payments_left_pending_or_processing_by_a_stopped_instance_are_processed_after_restart()
    {
        var database = PaymentApiFactory.NewDatabaseName();
        try
        {
            await RunScenarioAsync(database);
        }
        finally
        {
            await PaymentApiFactory.DropDatabaseAsync(database);
        }
    }

    private async Task RunScenarioAsync(string database)
    {
        Guid processingId;
        Guid pendingId;

        var first = new ConfiguredApiFactory(database, dropDatabaseOnDispose: false, new Dictionary<string, string?>
        {
            ["PaymentProcessing:WorkerCount"] = "1",
            ["PaymentProcessing:LeaseSeconds"] = "10",
            ["PaymentProcessing:PollIntervalSeconds"] = "60"
        });

        try
        {
            await first.InitializeAsync();
            var client = await LoginAsync(first);

            processingId = await CreateAsync(client, StubPaymentProviderClient.SlowAmount);
            await WaitForStatusAsync(first, processingId, "Processing");
            pendingId = await CreateAsync(client, 15m);

            var statesBeforeShutdown = await first.WithDbAsync(db => db.Payments
                .Where(p => p.Id == processingId || p.Id == pendingId)
                .ToDictionaryAsync(p => p.Id, p => p.Status.ToString()));
            output.WriteLine($"Before shutdown: {string.Join(", ", statesBeforeShutdown.Values)}");
            statesBeforeShutdown[processingId].Should().Be("Processing");
            statesBeforeShutdown[pendingId].Should().Be("Pending");
        }
        finally
        {
            await ((IAsyncLifetime)first).DisposeAsync();
        }

        var second = new ConfiguredApiFactory(database, dropDatabaseOnDispose: true, new Dictionary<string, string?>
        {
            ["PaymentProcessing:PollIntervalSeconds"] = "1"
        });

        try
        {
            await second.InitializeAsync();

            await WaitForStatusAsync(second, pendingId, "Completed");
            await WaitForStatusAsync(second, processingId, "Completed");

            var recovered = await second.WithDbAsync(db => db.Payments.AsNoTracking().Include(p => p.History).SingleAsync(p => p.Id == processingId));
            recovered.AttemptCount.Should().Be(2);
            recovered.History.Select(h => h.Reason).Should().Contain("Processing lease expired; returned to queue.");

            var pending = await second.WithDbAsync(db => db.Payments.AsNoTracking().SingleAsync(p => p.Id == pendingId));
            pending.AttemptCount.Should().Be(1);
        }
        finally
        {
            await ((IAsyncLifetime)second).DisposeAsync();
        }
    }

    private static async Task<HttpClient> LoginAsync(PaymentApiFactory factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "user@test.com", password = "User123!" });
        var token = (await login.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<Guid> CreateAsync(HttpClient client, decimal amount)
    {
        var response = await client.PostAsJsonAsync("/api/payments", new { providerId = PaymentApiFactory.FakePayId, amount, currency = "USD" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
    }

    private static async Task WaitForStatusAsync(PaymentApiFactory factory, Guid id, string status, int timeoutSeconds = 40)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        string current;
        do
        {
            await Task.Delay(200);
            current = await factory.WithDbAsync(async db => (await db.Payments.AsNoTracking().SingleAsync(p => p.Id == id)).Status.ToString());
        }
        while (current != status && DateTime.UtcNow < deadline);

        current.Should().Be(status, $"payment {id} should reach {status}");
    }
}
