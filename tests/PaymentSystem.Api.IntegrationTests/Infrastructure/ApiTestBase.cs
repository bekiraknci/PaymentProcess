using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace PaymentSystem.Api.IntegrationTests.Infrastructure;

public abstract class ApiTestBase(PaymentApiFactory factory)
{
    public const string AdminEmail = "admin@test.com";
    public const string AdminPassword = "Admin123!";
    public const string UserEmail = "user@test.com";
    public const string User2Email = "user2@test.com";
    public const string UserPassword = "User123!";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    protected PaymentApiFactory Factory { get; } = factory;

    protected HttpClient Anonymous() => Factory.CreateClient();

    protected async Task<HttpClient> ClientFor(string email, string password)
    {
        var client = Factory.CreateClient();
        var token = await TokenFor(email, password);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected Task<HttpClient> AsAdmin() => ClientFor(AdminEmail, AdminPassword);

    protected Task<HttpClient> AsUser() => ClientFor(UserEmail, UserPassword);

    protected Task<HttpClient> AsUser2() => ClientFor(User2Email, UserPassword);

    protected async Task<string> TokenFor(string email, string password)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadDataAsync<JsonElement>();
        return body.GetProperty("accessToken").GetString()!;
    }

    protected static object PaymentBody(decimal amount = 100m, string currency = "USD", string providerId = PaymentApiFactory.FakePayId) =>
        new { providerId, amount, currency };

    protected static async Task<HttpResponseMessage> PostPayment(HttpClient client, object body, string? idempotencyKey = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null)
        {
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    protected static async Task<PaymentDto> CreatePayment(HttpClient client, decimal amount = 100m, string currency = "USD")
    {
        var response = await PostPayment(client, PaymentBody(amount, currency));
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadDataAsync<PaymentDto>(Json))!;
    }

    protected static async Task<PaymentDto> WaitForFinalStatus(HttpClient client, Guid id, int timeoutSeconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        PaymentDto? payment;
        do
        {
            await Task.Delay(200);
            payment = await client.GetDataAsync<PaymentDto>($"/api/payments/{id}", Json);
        }
        while (payment!.Status is "Pending" or "Processing" && DateTime.UtcNow < deadline);

        return payment;
    }

    protected static async Task<JsonElement> ReadProblem(HttpResponseMessage response)
    {
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}

public sealed record PaymentDto(
    Guid Id,
    Guid UserId,
    Guid ProviderId,
    decimal Amount,
    string Currency,
    string Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt,
    string? ProviderTransactionId,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record PagedDto<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public sealed record HistoryDto(string? FromStatus, string ToStatus, string? Reason, DateTimeOffset ChangedAt, string? ChangedBy);
