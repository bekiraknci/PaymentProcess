using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Api.IntegrationTests.Infrastructure;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Api.IntegrationTests.EndToEnd;

[Collection(EndToEndCollection.Name)]
public sealed class EndToEndTests(EndToEndFactory factory) : ApiTestBase(factory)
{
    private EndToEndFactory E2E => (EndToEndFactory)Factory;

    private static async Task<PaymentDto> WaitUntil(HttpClient client, Guid id, Func<PaymentDto, bool> condition, int timeoutSeconds = 45)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        PaymentDto payment;
        do
        {
            await Task.Delay(250);
            payment = (await client.GetDataAsync<PaymentDto>($"/api/payments/{id}", Json))!;
        }
        while (!condition(payment) && DateTime.UtcNow < deadline);

        return payment;
    }

    private async Task<HistoryDto[]> History(HttpClient client, Guid id) =>
        (await client.GetDataAsync<HistoryDto[]>($"/api/payments/{id}/history", Json))!;

    [Fact]
    public async Task Approved_payment_flows_through_the_real_provider_and_completes()
    {
        var user = await AsUser();

        var created = await PostPayment(user, PaymentBody(125.50m, "USD"), Guid.NewGuid().ToString());
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = (await created.Content.ReadDataAsync<PaymentDto>(Json))!;

        var final = await WaitUntil(user, payment.Id, p => p.Status == "Completed");

        final.Status.Should().Be("Completed");
        final.AttemptCount.Should().Be(1);
        final.ProviderTransactionId.Should().StartWith("txn_");
        final.FailureReason.Should().BeNull();
        (await History(user, payment.Id)).Select(h => h.ToStatus).Should().Equal("Pending", "Processing", "Completed");
    }

    [Fact]
    public async Task Provider_replays_the_same_transaction_for_the_same_idempotency_key()
    {
        var provider = E2E.ProviderService.CreateClient();
        var key = Guid.NewGuid().ToString();

        async Task<JsonElement> Charge(decimal amount)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/charges")
            {
                Content = JsonContent.Create(new { amount, currency = "USD", reference = key })
            };
            request.Headers.Add("Idempotency-Key", key);
            var response = await provider.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }

        var first = await Charge(10m);
        var second = await Charge(10m);

        second.GetProperty("transactionId").GetString().Should().Be(first.GetProperty("transactionId").GetString());

        using var conflicting = new HttpRequestMessage(HttpMethod.Post, "/api/charges")
        {
            Content = JsonContent.Create(new { amount = 11m, currency = "USD", reference = key })
        };
        conflicting.Headers.Add("Idempotency-Key", key);
        (await provider.SendAsync(conflicting)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Provider_rejects_requests_without_idempotency_key()
    {
        var response = await E2E.ProviderService.CreateClient().PostAsJsonAsync("/api/charges", new { amount = 10m, currency = "USD" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Declined_payment_is_retried_automatically_until_the_attempt_cap_and_then_left_alone()
    {
        var user = await AsUser();
        var payment = await CreatePayment(user, 666m, "EUR");

        var final = await WaitUntil(user, payment.Id, p => p.Status == "Failed" && p.AttemptCount == 3 && p.NextAttemptAt is null);

        final.Status.Should().Be("Failed");
        final.AttemptCount.Should().Be(3);
        final.NextAttemptAt.Should().BeNull();
        final.FailureReason.Should().Contain("insufficient_funds");

        var history = await History(user, payment.Id);
        history.Where(h => h.ToStatus == "Processing").Should().HaveCount(3);
        history.Where(h => h.FromStatus == "Failed" && h.ToStatus == "Pending")
            .Select(h => h.Reason).Should().Equal(
                "Automatic retry scheduled (attempt 2 of 3).",
                "Automatic retry scheduled (attempt 3 of 3).");

        await Task.Delay(TimeSpan.FromSeconds(4));
        var later = (await user.GetDataAsync<PaymentDto>($"/api/payments/{payment.Id}", Json))!;
        later.AttemptCount.Should().Be(3, "the retry job must not pick up a payment that exhausted its attempts");
        later.Status.Should().Be("Failed");
    }

    [Fact]
    public async Task Transient_failure_is_recovered_by_the_retry_job()
    {
        var user = await AsUser();
        var payment = await CreatePayment(user, 502m, "USD");

        var final = await WaitUntil(user, payment.Id, p => p.Status == "Completed");

        final.Status.Should().Be("Completed");
        final.AttemptCount.Should().Be(2);
        final.ProviderTransactionId.Should().StartWith("txn_");
        (await History(user, payment.Id)).Select(h => h.ToStatus).Should().Equal(
            "Pending", "Processing", "Failed", "Pending", "Processing", "Completed");
    }

    [Fact]
    public async Task Provider_outage_fails_with_reason_after_http_retries()
    {
        var user = await AsUser();
        var payment = await CreatePayment(user, 503m, "GBP");

        var first = await WaitUntil(user, payment.Id, p => p.Status == "Failed");

        first.FailureReason.Should().Contain("HTTP 503");
        first.AttemptCount.Should().Be(1);
        first.NextAttemptAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Provider_timeout_fails_without_blocking_the_api()
    {
        var user = await AsUser();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var payment = await CreatePayment(user, 999m, "USD");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2), "submission must not wait for the provider");

        var first = await WaitUntil(user, payment.Id, p => p.Status == "Failed");

        first.FailureReason.Should().Contain("timed out");
    }

    [Fact]
    public async Task Admin_manual_retry_bypasses_the_attempt_cap()
    {
        var user = await AsUser();
        var admin = await AsAdmin();
        var payment = await CreatePayment(user, 666m, "USD");
        await WaitUntil(user, payment.Id, p => p.AttemptCount == 3 && p.NextAttemptAt is null && p.Status == "Failed");

        var retry = await admin.PostAsync($"/api/payments/{payment.Id}/retry", null);

        retry.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var final = await WaitUntil(user, payment.Id, p => p.AttemptCount == 4 && p.Status == "Failed");
        final.AttemptCount.Should().Be(4);
        final.NextAttemptAt.Should().BeNull();
    }

    [Fact]
    public async Task Complete_business_scenario_across_roles()
    {
        var admin = await AsAdmin();
        var user = await AsUser();
        var user2 = await AsUser2();

        var providerResponse = await admin.PostAsJsonAsync("/api/providers", new
        {
            code = $"E2E{Guid.NewGuid():N}"[..14],
            name = "E2E Pay",
            baseUrl = "http://localhost:5100",
            supportedCurrencies = new[] { "TRY", "USD" }
        });
        providerResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var providerId = (await providerResponse.Content.ReadDataAsync<JsonElement>()).GetProperty("id").GetString()!;

        var approved = await CreatePayment(user, 250m, "TRY");
        await PostPayment(user, PaymentBody(250m, "TRY", providerId));
        var byNewProvider = await PostPayment(user, PaymentBody(75m, "USD", providerId), "e2e-" + Guid.NewGuid());
        byNewProvider.StatusCode.Should().Be(HttpStatusCode.Created);
        var newProviderPayment = (await byNewProvider.Content.ReadDataAsync<PaymentDto>(Json))!;
        var unsupported = await PostPayment(user, PaymentBody(75m, "EUR", providerId));
        unsupported.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await WaitUntil(user, approved.Id, p => p.Status == "Completed")).Status.Should().Be("Completed");
        (await WaitUntil(user, newProviderPayment.Id, p => p.Status == "Completed")).Status.Should().Be("Completed");

        (await admin.PatchAsync($"/api/providers/{providerId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await PostPayment(user, PaymentBody(10m, "USD", providerId))).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await user2.GetAsync($"/api/payments/{approved.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var adminList = await admin.GetDataAsync<PagedDto<PaymentDto>>("/api/payments?pageSize=100", Json);
        adminList!.Items.Select(p => p.Id).Should().Contain([approved.Id, newProviderPayment.Id]);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await admin.GetDataAsync<JsonElement>(
            $"/api/reports/daily-payments?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&providerId={providerId}");
        var rows = report.GetProperty("rows").EnumerateArray().ToList();
        rows.Should().ContainSingle(r => r.GetProperty("status").GetString() == "Completed" && r.GetProperty("currency").GetString() == "TRY")
            .Which.GetProperty("totalAmount").GetDecimal().Should().Be(250m);
        rows.Should().ContainSingle(r => r.GetProperty("status").GetString() == "Completed" && r.GetProperty("currency").GetString() == "USD")
            .Which.GetProperty("paymentCount").GetInt64().Should().Be(1);
    }

    [Fact]
    public async Task Report_totals_match_the_database()
    {
        var user = await AsUser();
        var admin = await AsAdmin();
        var payments = new List<PaymentDto>();
        foreach (var amount in new[] { 11m, 22m, 666m, 33.33m })
        {
            payments.Add(await CreatePayment(user, amount, "USD"));
        }

        foreach (var p in payments)
        {
            await WaitUntil(user, p.Id, x => x.Status is "Completed" or "Failed");
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await admin.GetDataAsync<JsonElement>(
            $"/api/reports/daily-payments?from={today:yyyy-MM-dd}&to={today:yyyy-MM-dd}&providerId={PaymentApiFactory.FakePayId}");

        var expected = await Factory.WithDbAsync(db => db.Payments
            .Where(p => p.ProviderId == Guid.Parse(PaymentApiFactory.FakePayId) && p.Status == PaymentStatus.Completed)
            .GroupBy(p => new { p.Status, p.Money.Currency })
            .Select(g => new { g.Key.Status, g.Key.Currency, Count = g.LongCount(), Total = g.Sum(p => p.Money.Amount) })
            .ToListAsync());

        var rows = report.GetProperty("rows").EnumerateArray()
            .Select(r => new
            {
                Status = Enum.Parse<PaymentStatus>(r.GetProperty("status").GetString()!),
                Currency = r.GetProperty("currency").GetString()!,
                Count = r.GetProperty("paymentCount").GetInt64(),
                Total = r.GetProperty("totalAmount").GetDecimal()
            })
            .Where(r => r.Status == PaymentStatus.Completed)
            .GroupBy(r => new { r.Status, r.Currency })
            .Select(g => new { g.Key.Status, g.Key.Currency, Count = g.Sum(x => x.Count), Total = g.Sum(x => x.Total) })
            .ToList();

        rows.Should().BeEquivalentTo(expected);
    }
}
