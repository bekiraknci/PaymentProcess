using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Api.IntegrationTests.Infrastructure;
using Xunit.Abstractions;

namespace PaymentSystem.Api.IntegrationTests.Load;

[Collection(ApiCollection.Name)]
public sealed class LoadTests(PaymentApiFactory factory, ITestOutputHelper output) : ApiTestBase(factory)
{
    [Fact]
    public async Task Burst_of_identical_requests_with_one_idempotency_key_creates_exactly_one_payment()
    {
        var client = await AsUser();
        var key = "burst-" + Guid.NewGuid();

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => PostPayment(client, PaymentBody(77.70m), key)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var ids = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<PaymentDto>(Json))!.Id));
        ids.Distinct().Should().ContainSingle();
        responses.Count(r => !r.Headers.Contains("Idempotent-Replayed")).Should().Be(1);

        var rows = await Factory.WithDbAsync(db => db.Payments.CountAsync(p => p.IdempotencyKey == key));
        rows.Should().Be(1);

        await WaitForFinalStatus(client, ids[0]);
        Factory.Provider.CallsFor(ids[0]).Should().Be(1);
    }

    [Fact]
    public async Task High_volume_submissions_are_all_accepted_and_processed_exactly_once()
    {
        const int total = 600;
        var clients = new[] { await AsUser(), await AsUser2(), await AsAdmin() };
        var stopwatch = Stopwatch.StartNew();
        using var throttle = new SemaphoreSlim(64);

        var responses = await Task.WhenAll(Enumerable.Range(0, total).Select(async i =>
        {
            await throttle.WaitAsync();
            try
            {
                return await PostPayment(clients[i % clients.Length], PaymentBody(1m + i % 50), $"load-{Guid.NewGuid()}");
            }
            finally
            {
                throttle.Release();
            }
        }));

        var submitElapsed = stopwatch.Elapsed;
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        var ids = (await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<PaymentDto>(Json))!.Id))).ToHashSet();
        ids.Should().HaveCount(total);

        var deadline = DateTime.UtcNow.AddSeconds(90);
        int unfinished;
        do
        {
            await Task.Delay(500);
            unfinished = await Factory.WithDbAsync(db => db.Payments.CountAsync(p => ids.Contains(p.Id) && (p.Status == Domain.Payments.PaymentStatus.Pending || p.Status == Domain.Payments.PaymentStatus.Processing)));
        }
        while (unfinished > 0 && DateTime.UtcNow < deadline);

        var processedElapsed = stopwatch.Elapsed;
        unfinished.Should().Be(0);
        ids.Should().OnlyContain(id => Factory.Provider.CallsFor(id) == 1, "every payment must reach the provider exactly once");

        output.WriteLine($"Accepted {total} payments in {submitElapsed.TotalSeconds:F2}s ({total / submitElapsed.TotalSeconds:F0} req/s, 64 concurrent clients).");
        output.WriteLine($"All {total} processed end-to-end in {processedElapsed.TotalSeconds:F2}s ({total / processedElapsed.TotalSeconds:F0} payments/s).");
    }

    [Fact]
    public async Task Concurrent_manual_retries_of_the_same_payment_requeue_it_only_once()
    {
        var user = await AsUser();
        var admin = await AsAdmin();
        var payment = await CreatePayment(user, StubPaymentProviderClient.DeclineAmount);
        await WaitForFinalStatus(user, payment.Id);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => admin.PostAsync($"/api/payments/{payment.Id}/retry", null)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Accepted).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Accepted).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);

        await Task.Delay(300);
        var final = await WaitForFinalStatus(user, payment.Id);
        final.AttemptCount.Should().Be(2);
        Factory.Provider.CallsFor(payment.Id).Should().Be(2);
    }
}
