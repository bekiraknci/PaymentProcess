using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PaymentSystem.Api.IntegrationTests.Infrastructure;

namespace PaymentSystem.Api.IntegrationTests.Functional;

public sealed class ProviderTests(PaymentApiFactory factory) : ApiTestBase(factory)
{
    private static string NewCode() => $"T{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task Admin_lists_seeded_providers()
    {
        var client = await AsAdmin();

        var providers = await client.GetFromJsonAsync<JsonElement[]>("/api/providers");

        providers!.Select(p => p.GetProperty("code").GetString()).Should().Contain(["FAKEPAY", "LEGACYPAY"]);
        var fakePay = providers!.Single(p => p.GetProperty("code").GetString() == "FAKEPAY");
        fakePay.GetProperty("isActive").GetBoolean().Should().BeTrue();
        fakePay.GetProperty("supportedCurrencies").EnumerateArray().Select(c => c.GetString())
            .Should().Equal("EUR", "GBP", "TRY", "USD");
    }

    [Fact]
    public async Task Full_provider_lifecycle()
    {
        var client = await AsAdmin();
        var code = NewCode();

        var created = await client.PostAsJsonAsync("/api/providers", new
        {
            code = code.ToLowerInvariant(),
            name = "  Acme Pay ",
            baseUrl = "https://acme.test",
            supportedCurrencies = new[] { "usd", "EUR", "USD" },
            isActive = true
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.Location.Should().NotBeNull();
        var provider = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = provider.GetProperty("id").GetGuid();
        provider.GetProperty("code").GetString().Should().Be(code.ToUpperInvariant());
        provider.GetProperty("name").GetString().Should().Be("Acme Pay");
        provider.GetProperty("supportedCurrencies").EnumerateArray().Select(c => c.GetString()).Should().Equal("EUR", "USD");

        var fetched = await client.GetAsync(created.Headers.Location);
        fetched.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await client.PutAsJsonAsync($"/api/providers/{id}", new
        {
            name = "Acme Pay 2",
            baseUrl = (string?)null,
            supportedCurrencies = new[] { "TRY" }
        });
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString().Should().Be("Acme Pay 2");

        (await client.PatchAsync($"/api/providers/{id}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>($"/api/providers/{id}")).GetProperty("isActive").GetBoolean().Should().BeFalse();

        (await client.PatchAsync($"/api/providers/{id}/activate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetFromJsonAsync<JsonElement>($"/api/providers/{id}")).GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Duplicate_code_is_rejected_case_insensitively()
    {
        var client = await AsAdmin();

        var response = await client.PostAsJsonAsync("/api/providers", new { code = "fakepay", name = "Dup", supportedCurrencies = new[] { "USD" } });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblem(response)).GetProperty("code").GetString().Should().Be("provider.code_taken");
    }

    [Theory]
    [InlineData("", "Name", "USD", "code")]
    [InlineData("BAD CODE!", "Name", "USD", "code")]
    [InlineData("OKCODE", "", "USD", "name")]
    [InlineData("OKCODE", "Name", "XYZ", "supportedCurrencies")]
    public async Task Invalid_provider_returns_field_errors(string code, string name, string currency, string field)
    {
        var client = await AsAdmin();

        var response = await client.PostAsJsonAsync("/api/providers", new { code, name, supportedCurrencies = new[] { currency } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_base_url_is_rejected()
    {
        var client = await AsAdmin();

        var response = await client.PostAsJsonAsync("/api/providers", new
        {
            code = NewCode(), name = "X", baseUrl = "javascript:alert(1)", supportedCurrencies = new[] { "USD" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("GET", "/api/providers/{0}")]
    [InlineData("PUT", "/api/providers/{0}")]
    [InlineData("PATCH", "/api/providers/{0}/activate")]
    [InlineData("PATCH", "/api/providers/{0}/deactivate")]
    public async Task Unknown_provider_returns_404(string method, string template)
    {
        var client = await AsAdmin();
        var request = new HttpRequestMessage(new HttpMethod(method), string.Format(template, Guid.NewGuid()));
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(new { name = "X", supportedCurrencies = new[] { "USD" } });
        }

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("GET", "/api/providers")]
    [InlineData("GET", "/api/providers/" + PaymentApiFactory.FakePayId)]
    [InlineData("POST", "/api/providers")]
    [InlineData("PUT", "/api/providers/" + PaymentApiFactory.FakePayId)]
    [InlineData("PATCH", "/api/providers/" + PaymentApiFactory.FakePayId + "/deactivate")]
    public async Task Regular_user_cannot_manage_providers(string method, string url)
    {
        var client = await AsUser();
        var request = new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method is "POST" or "PUT" ? JsonContent.Create(new { code = "X", name = "X", supportedCurrencies = new[] { "USD" } }) : null
        };

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await ReadProblem(response);
    }
}
