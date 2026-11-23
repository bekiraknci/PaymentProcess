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

    [Fact]
    public async Task Seeing_every_users_payments_follows_the_view_all_permission_not_the_admin_role()
    {
        var user = await LoginAsync(ApiTestBase.UserEmail, ApiTestBase.UserPassword);
        var created = await Client(user.Token).PostAsJsonAsync("/api/payments", new { providerId = PaymentApiFactory.FakePayId, amount = 10m, currency = "USD" });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var paymentId = (await created.Content.ReadDataAsync<JsonElement>()).GetProperty("id").GetGuid();

        var admin = await LoginAsync(ApiTestBase.AdminEmail, ApiTestBase.AdminPassword);
        (await Client(admin.Token).GetAsync($"/api/payments/{paymentId}")).StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.ExecuteSqlAsync("DELETE FROM dbo.RolePermissions WHERE Role = N'Admin' AND PermissionName = 'Payments.ViewAll'");
        var restricted = await LoginAsync(ApiTestBase.AdminEmail, ApiTestBase.AdminPassword);
        (await Client(restricted.Token).GetAsync($"/api/payments/{paymentId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await _factory.ExecuteSqlAsync("INSERT INTO dbo.RolePermissions (Role, PermissionName) VALUES (N'User', 'Payments.ViewAll')");
        var user2 = await LoginAsync(ApiTestBase.User2Email, ApiTestBase.UserPassword);
        (await Client(user2.Token).GetAsync($"/api/payments/{paymentId}")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Report_access_follows_the_report_permission_not_the_admin_role()
    {
        const string report = "/api/reports/daily-payments?from=2026-01-01&to=2026-01-31";

        await _factory.ExecuteSqlAsync("INSERT INTO dbo.RolePermissions (Role, PermissionName) VALUES (N'User', 'Reports.View')");
        var user = await LoginAsync(ApiTestBase.UserEmail, ApiTestBase.UserPassword);
        (await Client(user.Token).GetAsync(report)).StatusCode.Should().Be(HttpStatusCode.OK);

        await _factory.ExecuteSqlAsync("DELETE FROM dbo.RolePermissions WHERE Role = N'Admin' AND PermissionName = 'Reports.View'");
        var admin = await LoginAsync(ApiTestBase.AdminEmail, ApiTestBase.AdminPassword);
        (await Client(admin.Token).GetAsync(report)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_permission_added_only_in_the_database_is_returned_by_login_and_me()
    {
        await _factory.ExecuteSqlAsync(
            """
            INSERT INTO dbo.Permissions (Name, Description) VALUES ('Payments.Export', N'Export payments.');
            INSERT INTO dbo.RolePermissions (Role, PermissionName) VALUES (N'User', 'Payments.Export');
            """);

        var login = await LoginAsync(ApiTestBase.UserEmail, ApiTestBase.UserPassword);
        login.Permissions.Should().Contain("Payments.Export");

        var me = await Client(login.Token).GetDataAsync<JsonElement>("/api/auth/me");
        me.GetProperty("permissions").EnumerateArray().Select(p => p.GetString())
            .Should().BeEquivalentTo(Permissions.PaymentsCreate, Permissions.PaymentsView, "Payments.Export");
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
