using System.Net;
using System.Text.Json;
using FluentAssertions;
using PaymentSystem.Api.IntegrationTests.Infrastructure;

namespace PaymentSystem.Api.IntegrationTests.Functional;

[Collection(ApiCollection.Name)]
public sealed class SwaggerTests(PaymentApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Swagger_ui_and_document_are_served()
    {
        var client = Anonymous();

        (await client.GetAsync("/swagger/index.html")).StatusCode.Should().Be(HttpStatusCode.OK);
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Document_describes_every_endpoint_security_and_examples()
    {
        var doc = JsonDocument.Parse(await Anonymous().GetStringAsync("/swagger/v1/swagger.json")).RootElement;
        var paths = doc.GetProperty("paths");

        paths.EnumerateObject().Select(p => p.Name).Should().Contain(
        [
            "/api/auth/login",
            "/api/auth/me",
            "/api/payments",
            "/api/payments/{id}",
            "/api/payments/{id}/history",
            "/api/payments/{id}/retry",
            "/api/providers",
            "/api/providers/{id}",
            "/api/providers/{id}/activate",
            "/api/providers/{id}/deactivate",
            "/api/reports/daily-payments"
        ]);

        doc.GetProperty("components").GetProperty("securitySchemes").GetProperty("Bearer")
            .GetProperty("scheme").GetString().Should().Be("bearer");

        var create = paths.GetProperty("/api/payments").GetProperty("post");
        create.GetProperty("summary").GetString().Should().NotBeNullOrWhiteSpace();
        create.GetProperty("parameters").EnumerateArray()
            .Should().Contain(p => p.GetProperty("name").GetString() == "Idempotency-Key" && p.GetProperty("in").GetString() == "header");
        create.GetProperty("responses").EnumerateObject().Select(r => r.Name)
            .Should().Contain(["201", "400", "401", "403", "404", "409", "429"]);

        var schemas = doc.GetProperty("components").GetProperty("schemas");
        schemas.GetProperty("CreatePaymentRequest").GetProperty("properties").GetProperty("amount")
            .GetProperty("example").GetDecimal().Should().Be(125.50m);
        schemas.GetProperty("LoginRequest").GetProperty("properties").GetProperty("email")
            .GetProperty("example").GetString().Should().Be("user@test.com");

        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                operation.Value.TryGetProperty("summary", out var summary).Should().BeTrue($"{operation.Name.ToUpperInvariant()} {path.Name} needs a summary");
                summary.GetString().Should().NotBeNullOrWhiteSpace();
            }
        }

        doc.GetProperty("info").GetProperty("description").GetString().Should().Contain("admin@test.com").And.Contain("666");
    }
}
