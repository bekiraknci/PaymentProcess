using System.Net.Http.Json;
using System.Text.Json;

namespace PaymentSystem.Api.IntegrationTests.Infrastructure;

public sealed record ApiEnvelope<T>(int StatusCode, bool IsSuccess, T Data);

public static class ApiResponseReader
{
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    public static async Task<T> ReadDataAsync<T>(this HttpContent content, JsonSerializerOptions? options = null)
    {
        var envelope = await content.ReadFromJsonAsync<ApiEnvelope<T>>(options ?? WebOptions)
            ?? throw new InvalidOperationException("The response body is empty.");
        return envelope.Data;
    }

    public static async Task<T> GetDataAsync<T>(this HttpClient client, string requestUri, JsonSerializerOptions? options = null)
    {
        using var response = await client.GetAsync(requestUri);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadDataAsync<T>(options);
    }
}
