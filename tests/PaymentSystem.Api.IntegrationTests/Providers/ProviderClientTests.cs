using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Application.Abstractions.Providers;
using PaymentSystem.Infrastructure;

namespace PaymentSystem.Api.IntegrationTests.Providers;

public sealed class ProviderClientTests
{
    private static readonly ProviderChargeRequest Request = new(Guid.CreateVersion7(), 10m, "USD");

    private sealed class StubHandler(HttpStatusCode status, string body, string contentType = "application/json") : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, contentType)
            });
        }
    }

    private static (IPaymentProviderClient Client, StubHandler Handler) Create(
        HttpStatusCode status,
        string body,
        int maxRetryAttempts = 1,
        string contentType = "application/json")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentProvider:BaseUrl"] = "http://provider.test",
                ["PaymentProvider:MaxRetryAttempts"] = maxRetryAttempts.ToString(),
                ["PaymentProvider:AttemptTimeoutSeconds"] = "2",
                ["PaymentProvider:TotalTimeoutSeconds"] = "5"
            })
            .Build();

        var handler = new StubHandler(status, body, contentType);
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => handler));

        var provider = services.BuildServiceProvider();
        return (provider.GetRequiredService<IPaymentProviderClient>(), handler);
    }

    [Fact]
    public async Task Approval_with_transaction_id_is_approved()
    {
        var (client, _) = Create(HttpStatusCode.OK, """{"status":"approved","transactionId":"txn_1"}""");

        var result = await client.ChargeAsync(Request, CancellationToken.None);

        result.Status.Should().Be(ProviderChargeStatus.Approved);
        result.TransactionId.Should().Be("txn_1");
    }

    [Fact]
    public async Task Approval_without_transaction_id_is_treated_as_unavailable()
    {
        var (client, _) = Create(HttpStatusCode.OK, """{"status":"approved"}""");

        var result = await client.ChargeAsync(Request, CancellationToken.None);

        result.Status.Should().Be(ProviderChargeStatus.Unavailable);
    }

    [Theory]
    [InlineData("not json at all", "text/plain")]
    [InlineData("{\"transactionId\": ", "application/json")]
    [InlineData("", "application/json")]
    public async Task Malformed_success_body_is_treated_as_unavailable_instead_of_throwing(string body, string contentType)
    {
        var (client, _) = Create(HttpStatusCode.OK, body, contentType: contentType);

        var result = await client.ChargeAsync(Request, CancellationToken.None);

        result.Status.Should().Be(ProviderChargeStatus.Unavailable);
        result.Reason.Should().Contain("invalid response");
    }

    [Fact]
    public async Task Decline_carries_the_provider_reason()
    {
        var (client, _) = Create(HttpStatusCode.PaymentRequired, """{"status":"declined","reason":"insufficient_funds"}""");

        var result = await client.ChargeAsync(Request, CancellationToken.None);

        result.Status.Should().Be(ProviderChargeStatus.Declined);
        result.Reason.Should().Be("insufficient_funds");
    }

    [Fact]
    public async Task Decline_with_unreadable_body_is_still_a_decline()
    {
        var (client, _) = Create(HttpStatusCode.PaymentRequired, "<html>declined</html>", contentType: "text/html");

        var result = await client.ChargeAsync(Request, CancellationToken.None);

        result.Status.Should().Be(ProviderChargeStatus.Declined);
        result.Reason.Should().Be("Declined");
    }

    [Fact]
    public async Task Non_retryable_client_error_is_unavailable_without_retries()
    {
        var (client, handler) = Create(HttpStatusCode.Conflict, "{}", maxRetryAttempts: 3);

        var result = await client.ChargeAsync(Request, CancellationToken.None);

        result.Status.Should().Be(ProviderChargeStatus.Unavailable);
        result.Reason.Should().Contain("409");
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Transient_error_is_retried_by_the_resilience_pipeline()
    {
        var (client, handler) = Create(HttpStatusCode.ServiceUnavailable, "{}", maxRetryAttempts: 2);

        var result = await client.ChargeAsync(Request, CancellationToken.None);

        result.Status.Should().Be(ProviderChargeStatus.Unavailable);
        handler.Calls.Should().Be(3);
    }

    [Fact]
    public async Task Zero_retry_attempts_disables_http_retries_instead_of_failing_every_call()
    {
        var (client, handler) = Create(HttpStatusCode.ServiceUnavailable, "{}", maxRetryAttempts: 0);

        var result = await client.ChargeAsync(Request, CancellationToken.None);

        result.Status.Should().Be(ProviderChargeStatus.Unavailable);
        handler.Calls.Should().Be(1);
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Circuit_opened_by_timeouts_is_reported_as_open_circuit_with_a_retry_after_hint()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PaymentProvider:BaseUrl"] = "http://provider.test",
                ["PaymentProvider:MaxRetryAttempts"] = "0",
                ["PaymentProvider:AttemptTimeoutSeconds"] = "1",
                ["PaymentProvider:TotalTimeoutSeconds"] = "5",
                ["PaymentProvider:CircuitBreakerMinimumThroughput"] = "2",
                ["PaymentProvider:CircuitBreakerBreakSeconds"] = "30"
            })
            .Build();
        var services = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(configuration);
        services.AddInfrastructure(configuration);
        services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => new HangingHandler()));
        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IPaymentProviderClient>();

        var first = await client.ChargeAsync(Request, CancellationToken.None);
        var second = await client.ChargeAsync(Request, CancellationToken.None);
        var third = await client.ChargeAsync(Request, CancellationToken.None);

        first.Reason.Should().Contain("timed out");
        second.Reason.Should().Contain("timed out");
        third.Status.Should().Be(ProviderChargeStatus.Unavailable);
        third.Reason.Should().Contain("circuit is open");
        third.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }
}
