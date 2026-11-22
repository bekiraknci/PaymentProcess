using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PaymentSystem.Api.IntegrationTests.Infrastructure;

namespace PaymentSystem.Api.IntegrationTests.Reports;

[Collection(ApiCollection.Name)]
public sealed class DailyReportTests(PaymentApiFactory factory) : ApiTestBase(factory)
{
    private sealed record ReportRow(DateOnly Date, Guid ProviderId, string ProviderName, string Status, string Currency, long PaymentCount, decimal TotalAmount);

    private sealed record CurrencyTotal(string Currency, long PaymentCount, decimal TotalAmount);

    private sealed record Report(DateOnly From, DateOnly To, Guid? ProviderId, IReadOnlyList<ReportRow> Rows, IReadOnlyList<CurrencyTotal> TotalsByCurrency);

    private async Task<string> CreateProviderAsync(string name)
    {
        var response = await (await AsAdmin()).PostAsJsonAsync("/api/providers", new
        {
            code = $"R{Guid.NewGuid():N}"[..12],
            name,
            supportedCurrencies = new[] { "USD", "EUR", "TRY" }
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task SeedAsync(string providerId, decimal amount, string currency, string createdAt)
    {
        var client = await AsUser();
        var response = await PostPayment(client, PaymentBody(amount, currency, providerId));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = (await response.Content.ReadFromJsonAsync<PaymentDto>(Json))!;
        await WaitForFinalStatus(client, payment.Id);
        await Factory.ExecuteSqlAsync($"UPDATE dbo.Payments SET CreatedAt = '{createdAt}' WHERE Id = '{payment.Id}'");
    }

    [Fact]
    public async Task Report_aggregates_per_utc_day_provider_status_and_currency()
    {
        var providerId = await CreateProviderAsync("Report Pay");
        await SeedAsync(providerId, 10.00m, "USD", "2026-10-01T08:00:00+00:00");
        await SeedAsync(providerId, 20.50m, "USD", "2026-10-01T23:59:59.9999999+00:00");
        await SeedAsync(providerId, StubPaymentProviderClient.DeclineAmount, "USD", "2026-10-01T12:00:00+00:00");
        await SeedAsync(providerId, 7.25m, "EUR", "2026-10-01T09:00:00+00:00");
        await SeedAsync(providerId, 100.00m, "USD", "2026-10-03T01:00:00+03:00");
        await SeedAsync(providerId, 1.00m, "USD", "2026-10-03T00:00:00+00:00");
        await SeedAsync(providerId, 2.00m, "TRY", "2026-09-30T23:59:59+00:00");

        var report = await (await AsAdmin()).GetFromJsonAsync<Report>(
            $"/api/reports/daily-payments?from=2026-10-01&to=2026-10-02&providerId={providerId}", Json);

        report!.Rows.Select(r => (r.Date.ToString("yyyy-MM-dd"), r.Status, r.Currency, r.PaymentCount, r.TotalAmount)).Should().Equal(
            ("2026-10-01", "Completed", "EUR", 1L, 7.25m),
            ("2026-10-01", "Completed", "USD", 2L, 30.50m),
            ("2026-10-01", "Failed", "USD", 1L, 666.00m),
            ("2026-10-02", "Completed", "USD", 1L, 100.00m));
        report.Rows.Should().OnlyContain(r => r.ProviderId == Guid.Parse(providerId) && r.ProviderName == "Report Pay");
        report.TotalsByCurrency.Should().BeEquivalentTo(new[]
        {
            new CurrencyTotal("EUR", 1, 7.25m),
            new CurrencyTotal("USD", 4, 796.50m)
        });
        report.From.Should().Be(new DateOnly(2026, 10, 1));
        report.To.Should().Be(new DateOnly(2026, 10, 2));
    }

    [Fact]
    public async Task Report_without_provider_filter_includes_all_providers()
    {
        var first = await CreateProviderAsync("Alpha Pay");
        var second = await CreateProviderAsync("Beta Pay");
        await SeedAsync(first, 5m, "USD", "2026-08-15T10:00:00+00:00");
        await SeedAsync(second, 6m, "USD", "2026-08-15T11:00:00+00:00");

        var report = await (await AsAdmin()).GetFromJsonAsync<Report>("/api/reports/daily-payments?from=2026-08-15&to=2026-08-15", Json);

        report!.Rows.Select(r => r.ProviderName).Should().Contain(["Alpha Pay", "Beta Pay"]);
        report.Rows.Should().BeInAscendingOrder(r => r.ProviderName);
        report.ProviderId.Should().BeNull();
    }

    [Fact]
    public async Task Empty_range_returns_empty_report()
    {
        var report = await (await AsAdmin()).GetFromJsonAsync<Report>("/api/reports/daily-payments?from=2001-01-01&to=2001-01-31", Json);

        report!.Rows.Should().BeEmpty();
        report.TotalsByCurrency.Should().BeEmpty();
    }

    [Theory]
    [InlineData("to=2026-10-01", "from")]
    [InlineData("from=2026-10-01", "to")]
    [InlineData("from=2026-10-05&to=2026-10-01", "to")]
    [InlineData("from=2025-01-01&to=2026-01-05", "to")]
    [InlineData("from=not-a-date&to=2026-10-01", "from")]
    public async Task Invalid_parameters_return_field_errors(string query, string field)
    {
        var response = await (await AsAdmin()).GetAsync($"/api/reports/daily-payments?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await ReadProblem(response)).GetProperty("errors");
        errors.EnumerateObject().Select(p => p.Name).Should().Contain(name => name.Equals(field, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Unknown_provider_filter_returns_404()
    {
        var response = await (await AsAdmin()).GetAsync($"/api/reports/daily-payments?from=2026-10-01&to=2026-10-02&providerId={Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Report_is_admin_only()
    {
        const string url = "/api/reports/daily-payments?from=2026-10-01&to=2026-10-02";

        (await Anonymous().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await (await AsUser()).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await (await AsAdmin()).GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Stored_procedure_is_deployed_by_migrations_and_callable_directly()
    {
        await using var connection = await Factory.OpenConnectionAsync();
        await using var exists = new Microsoft.Data.SqlClient.SqlCommand("SELECT OBJECT_ID(N'dbo.sp_GetDailyPaymentReport', N'P')", connection);
        (await exists.ExecuteScalarAsync()).Should().NotBe(DBNull.Value);

        await using var call = new Microsoft.Data.SqlClient.SqlCommand("EXEC dbo.sp_GetDailyPaymentReport @From = '2026-10-01', @To = '2026-10-01'", connection);
        await using var reader = await call.ExecuteReaderAsync();
        Enumerable.Range(0, reader.FieldCount).Select(reader.GetName)
            .Should().Equal("Date", "ProviderId", "ProviderName", "Status", "Currency", "PaymentCount", "TotalAmount");
    }
}
