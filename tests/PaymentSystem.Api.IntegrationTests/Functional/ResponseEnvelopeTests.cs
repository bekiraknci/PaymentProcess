using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PaymentSystem.Api.IntegrationTests.Infrastructure;

namespace PaymentSystem.Api.IntegrationTests.Functional;

[Collection(ApiCollection.Name)]
public sealed class ResponseEnvelopeTests(PaymentApiFactory factory) : ApiTestBase(factory)
{
    private static async Task<JsonElement> ReadEnvelope(HttpResponseMessage response, HttpStatusCode expected)
    {
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.EnumerateObject().Select(p => p.Name).Should().Equal("statusCode", "isSuccess", "data");
        body.GetProperty("statusCode").GetInt32().Should().Be((int)expected);
        body.GetProperty("isSuccess").GetBoolean().Should().BeTrue();
        return body.GetProperty("data");
    }

    [Fact]
    public async Task Login_wraps_the_token_in_the_envelope()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email = UserEmail, password = UserPassword });

        var data = await ReadEnvelope(response, HttpStatusCode.OK);
        data.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Create_get_list_and_history_wrap_their_payloads()
    {
        var client = await AsUser();

        var created = await ReadEnvelope(await PostPayment(client, PaymentBody(42m)), HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        (await ReadEnvelope(await client.GetAsync($"/api/payments/{id}"), HttpStatusCode.OK))
            .GetProperty("id").GetGuid().Should().Be(id);
        (await ReadEnvelope(await client.GetAsync("/api/payments?pageSize=5"), HttpStatusCode.OK))
            .GetProperty("items").ValueKind.Should().Be(JsonValueKind.Array);
        (await ReadEnvelope(await client.GetAsync($"/api/payments/{id}/history"), HttpStatusCode.OK))
            .ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Manual_retry_wraps_the_payment_with_202()
    {
        var user = await AsUser();
        var payment = await WaitForFinalStatus(user, (await CreatePayment(user, StubPaymentProviderClient.DeclineAmount)).Id);
        payment.Status.Should().Be("Failed");

        var data = await ReadEnvelope(await (await AsAdmin()).PostAsync($"/api/payments/{payment.Id}/retry", null), HttpStatusCode.Accepted);

        data.GetProperty("id").GetGuid().Should().Be(payment.Id);
    }

    [Fact]
    public async Task Errors_stay_problem_details_without_the_envelope()
    {
        var response = await (await AsUser()).GetAsync($"/api/payments/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await ReadProblem(response);
        problem.TryGetProperty("isSuccess", out _).Should().BeFalse();
        problem.GetProperty("status").GetInt32().Should().Be(404);
    }
}
