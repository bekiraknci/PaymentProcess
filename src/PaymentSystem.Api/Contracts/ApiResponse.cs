namespace PaymentSystem.Api.Contracts;

/// <summary>
/// Envelope for every successful response. Errors are returned as ProblemDetails instead.
/// </summary>
/// <typeparam name="T">Type of the payload.</typeparam>
/// <param name="StatusCode">HTTP status code of the response.</param>
/// <param name="IsSuccess">Always <c>true</c> for a successful response.</param>
/// <param name="Data">The payload returned by the endpoint.</param>
public sealed record ApiResponse<T>(int StatusCode, bool IsSuccess, T Data);
