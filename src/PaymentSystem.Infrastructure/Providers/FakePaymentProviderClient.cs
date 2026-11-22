using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentSystem.Application.Abstractions.Providers;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PaymentSystem.Infrastructure.Providers;

internal sealed class FakePaymentProviderClient(HttpClient httpClient, IOptions<PaymentProviderOptions> options, ILogger<FakePaymentProviderClient> logger)
    : IPaymentProviderClient
{
    public async Task<ProviderChargeResult> ChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "api/charges")
        {
            Content = JsonContent.Create(new ChargeRequestBody(request.Amount, request.Currency, request.PaymentId.ToString()))
        };
        message.Headers.Add("Idempotency-Key", request.PaymentId.ToString());

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<ChargeResponseBody>(cancellationToken);
                return string.IsNullOrWhiteSpace(body?.TransactionId)
                    ? ProviderChargeResult.Unavailable("Provider returned an approval without a transaction id.")
                    : ProviderChargeResult.Approved(body.TransactionId);
            }

            if (response.StatusCode == HttpStatusCode.PaymentRequired)
            {
                var body = await response.Content.ReadFromJsonAsync<ChargeResponseBody>(cancellationToken);
                return ProviderChargeResult.Declined(body?.Reason ?? "Declined");
            }

            return ProviderChargeResult.Unavailable($"Provider responded with HTTP {(int)response.StatusCode}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsTimeout(ex))
        {
            logger.LogWarning("Provider call for payment {PaymentId} timed out.", request.PaymentId);
            return ProviderChargeResult.Unavailable("Provider request timed out.");
        }
        catch (BrokenCircuitException)
        {
            logger.LogWarning("Provider circuit is open; payment {PaymentId} not sent.", request.PaymentId);
            var retryAfter = TimeSpan.FromSeconds(options.Value.CircuitBreakerBreakSeconds);
            return ProviderChargeResult.Unavailable("Provider circuit is open.", retryAfter);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Provider is unreachable for payment {PaymentId}: {Error}", request.PaymentId, ex.Message);
            return ProviderChargeResult.Unavailable("Provider is unreachable.");
        }
    }

    private static bool IsTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TimeoutRejectedException or TimeoutException or OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private sealed record ChargeRequestBody(decimal Amount, string Currency, string Reference);

    private sealed record ChargeResponseBody(string? Status, string? TransactionId, string? Reason);
}
