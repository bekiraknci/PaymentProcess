using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PaymentSystem.Api.IntegrationTests.Infrastructure;

namespace PaymentSystem.Api.IntegrationTests.Functional;

public sealed class PaymentTests(PaymentApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Create_returns_201_with_location_and_pending_payment_then_completes_in_background()
    {
        var client = await AsUser();

        var response = await PostPayment(client, PaymentBody(125.50m, "usd"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var payment = (await response.Content.ReadFromJsonAsync<PaymentDto>(Json))!;
        response.Headers.Location!.AbsolutePath.Should().Be($"/api/payments/{payment.Id}");
        payment.Status.Should().BeOneOf("Pending", "Processing", "Completed");
        payment.Amount.Should().Be(125.50m);
        payment.Currency.Should().Be("USD");
        payment.AttemptCount.Should().BeLessThanOrEqualTo(1);

        var final = await WaitForFinalStatus(client, payment.Id);
        final.Status.Should().Be("Completed");
        final.ProviderTransactionId.Should().Be($"txn_{payment.Id:N}");
        final.AttemptCount.Should().Be(1);
        Factory.Provider.CallsFor(payment.Id).Should().Be(1);
    }

    [Fact]
    public async Task Declined_payment_fails_with_reason_and_next_attempt()
    {
        var client = await AsUser();
        var payment = await CreatePayment(client, StubPaymentProviderClient.DeclineAmount, "EUR");

        var final = await WaitForFinalStatus(client, payment.Id);

        final.Status.Should().Be("Failed");
        final.FailureReason.Should().Contain("insufficient_funds");
        final.NextAttemptAt.Should().BeAfter(final.UpdatedAt!.Value);
    }

    [Theory]
    [InlineData("0", "USD", "amount")]
    [InlineData("-1", "USD", "amount")]
    [InlineData("10.555", "USD", "amount")]
    [InlineData("100000000000000000", "USD", "amount")]
    [InlineData("10", "XYZ", "currency")]
    [InlineData("10", "US", "currency")]
    [InlineData("10", "", "currency")]
    public async Task Invalid_amount_or_currency_returns_field_errors(string amount, string currency, string field)
    {
        var client = await AsUser();
        var json = $"{{\"providerId\":\"{PaymentApiFactory.FakePayId}\",\"amount\":{amount},\"currency\":\"{currency}\"}}";

        var response = await client.PostAsync("/api/payments", new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"providerId\":\"" + PaymentApiFactory.FakePayId + "\",\"amount\":\"abc\",\"currency\":\"USD\"}")]
    [InlineData("{\"amount\":10,\"currency\":\"USD\"}")]
    [InlineData("{}")]
    [InlineData("not json")]
    [InlineData("")]
    public async Task Malformed_or_incomplete_body_returns_400(string json)
    {
        var client = await AsUser();

        var response = await client.PostAsync("/api/payments", new StringContent(json, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await ReadProblem(response);
    }

    [Fact]
    public async Task Unknown_provider_returns_404()
    {
        var client = await AsUser();

        var response = await PostPayment(client, PaymentBody(providerId: Guid.NewGuid().ToString()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ReadProblem(response)).GetProperty("code").GetString().Should().Be("provider.not_found");
    }

    [Fact]
    public async Task Inactive_provider_returns_400()
    {
        var client = await AsUser();

        var response = await PostPayment(client, PaymentBody(currency: "TRY", providerId: PaymentApiFactory.LegacyPayId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).GetProperty("errors").TryGetProperty("providerId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Currency_not_supported_by_provider_returns_400()
    {
        var admin = await AsAdmin();
        var created = await admin.PostAsJsonAsync("/api/providers", new
        {
            code = $"U{Guid.NewGuid():N}"[..12], name = "UsdOnly", supportedCurrencies = new[] { "USD" }
        });
        var providerId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var response = await PostPayment(await AsUser(), PaymentBody(currency: "EUR", providerId: providerId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).GetProperty("errors").TryGetProperty("currency", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Same_idempotency_key_and_body_replays_the_original_payment()
    {
        var client = await AsUser();
        var key = Guid.NewGuid().ToString();

        var first = await PostPayment(client, PaymentBody(42.10m), key);
        var second = await PostPayment(client, PaymentBody(42.10m), key);

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
        first.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        second.Headers.GetValues("Idempotent-Replayed").Should().Equal("true");
        var a = await first.Content.ReadFromJsonAsync<PaymentDto>(Json);
        var b = await second.Content.ReadFromJsonAsync<PaymentDto>(Json);
        b!.Id.Should().Be(a!.Id);

        var count = await Factory.WithDbAsync(db => Task.FromResult(db.Payments.Count(p => p.IdempotencyKey == key)));
        count.Should().Be(1);
    }

    [Fact]
    public async Task Equivalent_bodies_with_different_formatting_are_treated_as_the_same_request()
    {
        var client = await AsUser();
        var key = Guid.NewGuid().ToString();

        var first = await PostPayment(client, new { providerId = PaymentApiFactory.FakePayId, amount = 10m, currency = "usd" }, key);
        var second = await PostPayment(client, new { providerId = PaymentApiFactory.FakePayId.ToUpperInvariant(), amount = 10.00m, currency = "USD" }, key);

        second.StatusCode.Should().Be(HttpStatusCode.Created);
        second.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        (await second.Content.ReadFromJsonAsync<PaymentDto>(Json))!.Id
            .Should().Be((await first.Content.ReadFromJsonAsync<PaymentDto>(Json))!.Id);
    }

    [Fact]
    public async Task Same_idempotency_key_with_different_body_returns_409()
    {
        var client = await AsUser();
        var key = Guid.NewGuid().ToString();
        await PostPayment(client, PaymentBody(10m), key);

        var response = await PostPayment(client, PaymentBody(11m), key);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblem(response)).GetProperty("code").GetString().Should().Be("payment.idempotency_key_reused");
    }

    [Fact]
    public async Task Idempotency_keys_are_scoped_per_user()
    {
        var key = Guid.NewGuid().ToString();

        var a = await PostPayment(await AsUser(), PaymentBody(10m), key);
        var b = await PostPayment(await AsUser2(), PaymentBody(10m), key);

        b.StatusCode.Should().Be(HttpStatusCode.Created);
        b.Headers.Contains("Idempotent-Replayed").Should().BeFalse();
        (await b.Content.ReadFromJsonAsync<PaymentDto>(Json))!.Id
            .Should().NotBe((await a.Content.ReadFromJsonAsync<PaymentDto>(Json))!.Id);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("tab\tkey")]
    public async Task Invalid_idempotency_key_returns_400(string key)
    {
        var response = await PostPayment(await AsUser(), PaymentBody(), key);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Too_long_idempotency_key_returns_400()
    {
        var response = await PostPayment(await AsUser(), PaymentBody(), new string('k', 129));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadProblem(response)).GetProperty("errors").TryGetProperty("idempotencyKey", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Users_cannot_see_each_others_payments()
    {
        var mine = await CreatePayment(await AsUser());
        var other = await AsUser2();

        (await other.GetAsync($"/api/payments/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.GetAsync($"/api/payments/{mine.Id}/history")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var list = await other.GetFromJsonAsync<PagedDto<PaymentDto>>("/api/payments?pageSize=100", Json);
        list!.Items.Should().NotContain(p => p.Id == mine.Id);
        list.Items.Should().OnlyContain(p => p.UserId != mine.UserId);
    }

    [Fact]
    public async Task Admin_sees_all_payments()
    {
        var mine = await CreatePayment(await AsUser());
        var admin = await AsAdmin();

        (await admin.GetAsync($"/api/payments/{mine.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await admin.GetFromJsonAsync<PagedDto<PaymentDto>>("/api/payments?pageSize=100", Json);
        list!.Items.Should().Contain(p => p.Id == mine.Id);
    }

    [Fact]
    public async Task Unknown_payment_returns_404()
    {
        var client = await AsAdmin();

        (await client.GetAsync($"/api/payments/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/payments/not-a-guid")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task List_supports_paging_status_filter_and_newest_first_ordering()
    {
        var client = await AsUser2();
        var created = new List<PaymentDto>();
        for (var i = 0; i < 5; i++)
        {
            created.Add(await CreatePayment(client, 10m + i));
        }

        foreach (var p in created)
        {
            await WaitForFinalStatus(client, p.Id);
        }

        var page1 = await client.GetFromJsonAsync<PagedDto<PaymentDto>>("/api/payments?status=Completed&page=1&pageSize=2", Json);
        var page2 = await client.GetFromJsonAsync<PagedDto<PaymentDto>>("/api/payments?status=Completed&page=2&pageSize=2", Json);

        page1!.Items.Should().HaveCount(2);
        page1.TotalCount.Should().BeGreaterThanOrEqualTo(5);
        page1.TotalPages.Should().Be((int)Math.Ceiling(page1.TotalCount / 2.0));
        page1.Items.Should().OnlyContain(p => p.Status == "Completed");
        page1.Items.Should().BeInDescendingOrder(p => p.CreatedAt);
        page2!.Items.Select(p => p.Id).Should().NotIntersectWith(page1.Items.Select(p => p.Id));
        page1.Items[1].CreatedAt.Should().BeOnOrAfter(page2.Items[0].CreatedAt);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("status=Unknown")]
    public async Task Invalid_list_parameters_return_400(string query)
    {
        var response = await (await AsUser()).GetAsync($"/api/payments?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task History_lists_transitions_in_order_with_actor()
    {
        var client = await AsUser();
        var payment = await CreatePayment(client);
        await WaitForFinalStatus(client, payment.Id);

        var history = (await client.GetFromJsonAsync<HistoryDto[]>($"/api/payments/{payment.Id}/history", Json))!;

        history.Select(h => h.ToStatus).Should().Equal("Pending", "Processing", "Completed");
        history[0].FromStatus.Should().BeNull();
        history[0].ChangedBy.Should().Be(UserEmail);
        history[1].ChangedBy.Should().Be("system");
        history.Should().BeInAscendingOrder(h => h.ChangedAt);
    }

    [Fact]
    public async Task Admin_can_retry_failed_payment_and_it_is_processed_again()
    {
        var user = await AsUser();
        var payment = await CreatePayment(user, StubPaymentProviderClient.DeclineAmount);
        await WaitForFinalStatus(user, payment.Id);

        var response = await (await AsAdmin()).PostAsync($"/api/payments/{payment.Id}/retry", null);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location!.AbsolutePath.Should().Be($"/api/payments/{payment.Id}");
        await Task.Delay(300);
        var final = await WaitForFinalStatus(user, payment.Id);
        final.AttemptCount.Should().Be(2);
        Factory.Provider.CallsFor(payment.Id).Should().Be(2);

        var history = await user.GetFromJsonAsync<HistoryDto[]>($"/api/payments/{payment.Id}/history", Json);
        history!.Should().Contain(h => h.FromStatus == "Failed" && h.ToStatus == "Pending" && h.ChangedBy == AdminEmail);
    }

    [Fact]
    public async Task Retry_rules()
    {
        var user = await AsUser();
        var admin = await AsAdmin();
        var completed = await CreatePayment(user);
        await WaitForFinalStatus(user, completed.Id);
        var failed = await CreatePayment(user, StubPaymentProviderClient.DeclineAmount);
        await WaitForFinalStatus(user, failed.Id);

        (await user.PostAsync($"/api/payments/{failed.Id}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.PostAsync($"/api/payments/{completed.Id}/retry", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsync($"/api/payments/{Guid.NewGuid()}/retry", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Completed_payment_is_never_sent_to_the_provider_again()
    {
        var user = await AsUser();
        var payment = await CreatePayment(user);
        await WaitForFinalStatus(user, payment.Id);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        Factory.Provider.CallsFor(payment.Id).Should().Be(1);
    }

    [Fact]
    public async Task Deactivating_provider_after_submission_does_not_block_processing_of_accepted_payments()
    {
        var admin = await AsAdmin();
        var created = await admin.PostAsJsonAsync("/api/providers", new
        {
            code = $"D{Guid.NewGuid():N}"[..12], name = "Temp", supportedCurrencies = new[] { "USD" }
        });
        var providerId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
        var user = await AsUser();
        var response = await PostPayment(user, PaymentBody(StubPaymentProviderClient.SlowAmount, providerId: providerId));
        var payment = (await response.Content.ReadFromJsonAsync<PaymentDto>(Json))!;

        await admin.PatchAsync($"/api/providers/{providerId}/deactivate", null);

        (await WaitForFinalStatus(user, payment.Id)).Status.Should().Be("Completed");
    }
}
