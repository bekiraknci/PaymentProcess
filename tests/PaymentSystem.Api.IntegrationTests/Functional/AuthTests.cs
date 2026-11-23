using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PaymentSystem.Api.IntegrationTests.Infrastructure;

namespace PaymentSystem.Api.IntegrationTests.Functional;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(PaymentApiFactory factory) : ApiTestBase(factory)
{
    [Theory]
    [InlineData(AdminEmail, AdminPassword, "Admin", 6)]
    [InlineData(UserEmail, UserPassword, "User", 2)]
    [InlineData("  USER@Test.com ", UserPassword, "User", 2)]
    public async Task Login_with_valid_credentials_returns_token_and_permissions(string email, string password, string role, int permissionCount)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadDataAsync<JsonElement>();
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrEmpty();
        body.GetProperty("tokenType").GetString().Should().Be("Bearer");
        body.GetProperty("role").GetString().Should().Be(role);
        body.GetProperty("permissions").GetArrayLength().Should().Be(permissionCount);
        body.GetProperty("expiresAt").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(AdminEmail, "wrong-password")]
    [InlineData("nobody@test.com", "Whatever123!")]
    public async Task Login_with_bad_credentials_returns_401_without_revealing_which_part_was_wrong(string email, string password)
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email, password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await ReadProblem(response);
        problem.GetProperty("code").GetString().Should().Be("auth.invalid_credentials");
        problem.GetProperty("detail").GetString().Should().Be("Email or password is incorrect.");
    }

    [Fact]
    public async Task Login_with_malformed_input_returns_field_errors()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email = "not-an-email", password = "" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await ReadProblem(response);
        problem.GetProperty("errors").TryGetProperty("email", out _).Should().BeTrue();
        problem.GetProperty("errors").TryGetProperty("password", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Login_with_malformed_json_returns_400_problem()
    {
        var response = await Anonymous().PostAsync("/api/auth/login", new StringContent("{\"email\":", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ReadProblem(response);
    }

    [Fact]
    public async Task Me_returns_the_identity_of_the_caller()
    {
        var client = await AsUser();

        var body = await client.GetDataAsync<JsonElement>("/api/auth/me");

        body.GetProperty("email").GetString().Should().Be(UserEmail);
        body.GetProperty("role").GetString().Should().Be("User");
        body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo("Payments.Create", "Payments.View");
    }

    [Fact]
    public async Task Duplicated_bearer_prefix_is_tolerated()
    {
        var token = await TokenFor(UserEmail, UserPassword);
        var client = Anonymous();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"Bearer Bearer {token}");

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/payments")]
    [InlineData("POST", "/api/payments")]
    [InlineData("GET", "/api/providers")]
    [InlineData("POST", "/api/payments/0196a000-0000-7000-8000-000000000001/retry")]
    public async Task Protected_endpoints_return_401_problem_without_token(string method, string url)
    {
        var response = await Anonymous().SendAsync(new HttpRequestMessage(new HttpMethod(method), url)
        {
            Content = method == "POST" ? JsonContent.Create(new { }) : null
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await ReadProblem(response);
    }

    [Fact]
    public async Task Garbage_token_returns_401()
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        (await client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
