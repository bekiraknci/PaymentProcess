using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using PaymentSystem.Application.Abstractions.Security;
using Microsoft.IdentityModel.Tokens;
using PaymentSystem.Api.IntegrationTests.Infrastructure;

namespace PaymentSystem.Api.IntegrationTests.Security;

public sealed class SecurityTests(PaymentApiFactory factory) : ApiTestBase(factory)
{
    private const string AdminSub = "0196a000-0000-7000-8000-000000000101";

    private string ForgeToken(
        string? signingKey = null,
        string issuer = "PaymentSystem",
        string audience = "PaymentSystem.Api",
        DateTime? expires = null,
        string algorithm = SecurityAlgorithms.HmacSha256)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, AdminSub),
            new(JwtRegisteredClaimNames.Email, AdminEmail),
            new("role", "Admin"),
            new("permission", "Providers.Manage"),
            new("permission", "Payments.View"),
            new("permission", "Payments.Process")
        };
        var now = DateTime.UtcNow;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? Factory.SigningKey));
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = (expires ?? now.AddMinutes(10)).AddMinutes(-20),
            NotBefore = (expires ?? now.AddMinutes(10)).AddMinutes(-20),
            Expires = expires ?? now.AddMinutes(10),
            SigningCredentials = new SigningCredentials(key, algorithm)
        });
    }

    private async Task<HttpStatusCode> CallProvidersWith(string token)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetAsync("/api/providers")).StatusCode;
    }

    [Fact]
    public async Task Correctly_signed_token_is_accepted_as_a_baseline()
    {
        (await CallProvidersWith(ForgeToken())).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_signed_with_a_different_key_is_rejected()
    {
        (await CallProvidersWith(ForgeToken(signingKey: "attacker-controlled-key-0123456789-abcdefgh"))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        (await CallProvidersWith(ForgeToken(expires: DateTime.UtcNow.AddMinutes(-5)))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("SomeoneElse", "PaymentSystem.Api")]
    [InlineData("PaymentSystem", "SomeOtherApi")]
    public async Task Token_with_wrong_issuer_or_audience_is_rejected(string issuer, string audience)
    {
        (await CallProvidersWith(ForgeToken(issuer: issuer, audience: audience))).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void Token_validation_pins_the_signing_algorithm_and_requires_signed_expiring_tokens()
    {
        var options = Factory.Services
            .GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme)
            .TokenValidationParameters;

        options.ValidAlgorithms.Should().Equal(SecurityAlgorithms.HmacSha256);
        options.RequireSignedTokens.Should().BeTrue();
        options.RequireExpirationTime.Should().BeTrue();
        options.ValidateLifetime.Should().BeTrue();
        options.ClockSkew.Should().BeLessThanOrEqualTo(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Unsigned_alg_none_token_is_rejected()
    {
        static string B64(string s) => Base64UrlEncoder.Encode(s);
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var token = $"{B64("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{B64($"{{\"sub\":\"{AdminSub}\",\"role\":\"Admin\",\"permission\":\"Providers.Manage\",\"iss\":\"PaymentSystem\",\"aud\":\"PaymentSystem.Api\",\"exp\":{exp}}}")}.";

        (await CallProvidersWith(token)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Tampered_payload_invalidates_the_signature()
    {
        var userToken = await TokenFor(UserEmail, UserPassword);
        var parts = userToken.Split('.');
        var payload = Encoding.UTF8.GetString(Base64UrlEncoder.DecodeBytes(parts[1]))
            .Replace("\"role\":\"User\"", "\"role\":\"Admin\"")
            .Replace("\"Payments.View\"", "\"Providers.Manage\"");
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload)}.{parts[2]}";

        (await CallProvidersWith(tampered)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task User_cannot_read_or_retry_another_users_payment_by_guessing_its_id()
    {
        var victim = await CreatePayment(await AsUser(), StubPaymentProviderClient.DeclineAmount);
        await WaitForFinalStatus(await AsUser(), victim.Id);
        var attacker = await AsUser2();

        (await attacker.GetAsync($"/api/payments/{victim.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await attacker.GetAsync($"/api/payments/{victim.Id}/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await attacker.PostAsync($"/api/payments/{victim.Id}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Mass_assignment_of_server_controlled_fields_is_ignored()
    {
        var user = await AsUser();
        var victimUserId = "0196a000-0000-7000-8000-000000000103";

        var response = await PostPayment(user, new
        {
            providerId = PaymentApiFactory.FakePayId,
            amount = 10m,
            currency = "USD",
            id = Guid.Empty,
            userId = victimUserId,
            status = "Completed",
            attemptCount = 99,
            providerTransactionId = "txn_forged"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = (await response.Content.ReadFromJsonAsync<PaymentDto>(Json))!;
        payment.UserId.Should().NotBe(Guid.Parse(victimUserId));
        payment.Id.Should().NotBeEmpty();
        payment.AttemptCount.Should().BeLessThanOrEqualTo(1);
        payment.ProviderTransactionId.Should().NotBe("txn_forged");
    }

    [Theory]
    [InlineData("'; DROP TABLE Payments; --")]
    [InlineData("1' OR '1'='1")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Injection_payloads_are_treated_as_data(string payload)
    {
        var user = await AsUser();

        var asCurrency = await PostPayment(user, new { providerId = PaymentApiFactory.FakePayId, amount = 10m, currency = payload });
        var asKey = await PostPayment(user, PaymentBody(), payload.Replace(" ", string.Empty));
        var asProviderCode = await (await AsAdmin()).PostAsJsonAsync("/api/providers", new { code = payload, name = payload, supportedCurrencies = new[] { "USD" } });

        asCurrency.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        asKey.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.BadRequest);
        asProviderCode.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var paymentsTableStillExists = await Factory.WithDbAsync(db => Task.FromResult(db.Payments.Any()));
        paymentsTableStillExists.Should().BeTrue();
    }

    [Fact]
    public async Task Error_responses_do_not_leak_internal_details()
    {
        var response = await (await AsUser()).GetAsync($"/api/payments/{Guid.NewGuid()}");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContainAny("Exception", "StackTrace", "at PaymentSystem", "SqlClient", "Server=");
        var problem = JsonDocument.Parse(body).RootElement;
        problem.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Api_responses_carry_security_headers()
    {
        var response = await (await AsUser()).GetAsync("/api/payments");

        response.Headers.GetValues("X-Content-Type-Options").Should().Equal("nosniff");
        response.Headers.GetValues("X-Frame-Options").Should().Equal("DENY");
        response.Headers.GetValues("Referrer-Policy").Should().Equal("no-referrer");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.Contains("Server").Should().BeFalse();
    }

    [Fact]
    public async Task Health_endpoints_are_anonymous_and_report_database_status()
    {
        var client = Anonymous();

        (await client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ready.Content.ReadAsStringAsync()).Should().Be("Healthy");
    }
}

public sealed class RateLimitingTests : IClassFixture<RateLimitedApiFactory>
{
    private readonly RateLimitedApiFactory _factory;

    public RateLimitingTests(RateLimitedApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Login_is_throttled_per_client_with_retry_after()
    {
        var client = _factory.CreateClient();
        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? last = null;

        for (var i = 0; i < 7; i++)
        {
            last = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@test.com", password = "wrong" });
            statuses.Add(last.StatusCode);
        }

        statuses.Take(5).Should().OnlyContain(s => s == HttpStatusCode.Unauthorized);
        statuses.Skip(5).Should().OnlyContain(s => s == HttpStatusCode.TooManyRequests);
        last!.Headers.RetryAfter.Should().NotBeNull();
        last.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task Payment_submission_is_throttled_per_user()
    {
        var user = await _factory.WithDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Email == "user2@test.com"));
        var token = _factory.Services.GetRequiredService<IJwtTokenService>().CreateToken(user).Token;
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ =>
            client.PostAsJsonAsync("/api/payments", new { providerId = PaymentApiFactory.FakePayId, amount = 10m, currency = "USD" })));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().BeInRange(5, 7);
        responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).Should().BeGreaterThanOrEqualTo(5);
    }
}
