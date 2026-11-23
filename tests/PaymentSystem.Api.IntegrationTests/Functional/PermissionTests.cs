using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PaymentSystem.Api.IntegrationTests.Infrastructure;
using PaymentSystem.Application.Authorization;

namespace PaymentSystem.Api.IntegrationTests.Functional;

public sealed class PermissionTests : IAsyncLifetime
{
    private readonly ConfiguredApiFactory _factory = new(null, dropDatabaseOnDispose: true, null);

    public Task InitializeAsync() => _factory.InitializeAsync();

    public Task DisposeAsync() => ((IAsyncLifetime)_factory).DisposeAsync();

    [Fact]
    public async Task Revoking_a_permission_in_the_database_takes_effect_on_the_next_login()
    {
        var before = await LoginAsync(ApiTestBase.UserEmail, ApiTestBase.UserPassword);
        before.Permissions.Should().Contain(Permissions.PaymentsCreate);

        await _factory.ExecuteSqlAsync("DELETE FROM dbo.RolePermissions WHERE Role = N'User' AND PermissionName = 'Payments.Create'");

        var after = await LoginAsync(ApiTestBase.UserEmail, ApiTestBase.UserPassword);
        after.Permissions.Should().BeEquivalentTo(Permissions.PaymentsView);

        var response = await Client(after.Token).PostAsJsonAsync("/api/payments", new { providerId = PaymentApiFactory.FakePayId, amount = 10m, currency = "USD" });
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var admin = await LoginAsync(ApiTestBase.AdminEmail, ApiTestBase.AdminPassword);
        admin.Permissions.Should().BeEquivalentTo(Permissions.All);
    }

    [Fact]
    public async Task Granting_a_permission_in_the_database_opens_the_matching_endpoint()
    {
        var before = await LoginAsync(ApiTestBase.UserEmail, ApiTestBase.UserPassword);
        (await Client(before.Token).GetAsync("/api/providers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await _factory.ExecuteSqlAsync("INSERT INTO dbo.RolePermissions (Role, PermissionName) VALUES (N'User', 'Providers.Manage')");

        var after = await LoginAsync(ApiTestBase.UserEmail, ApiTestBase.UserPassword);
        after.Permissions.Should().Contain(Permissions.ProvidersManage);

        var me = await Client(after.Token).GetDataAsync<JsonElement>("/api/auth/me");
        me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()).Should().Contain(Permissions.ProvidersManage);
        (await Client(after.Token).GetAsync("/api/providers")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private HttpClient Client(string token)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<(string Token, IReadOnlyList<string> Permissions)> LoginAsync(string email, string password)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadDataAsync<JsonElement>();
        var permissions = body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToList();
        return (body.GetProperty("accessToken").GetString()!, permissions);
    }
}
