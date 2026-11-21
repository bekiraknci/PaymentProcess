using FakePaymentProvider.Api;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ScenarioOptions>()
    .Bind(builder.Configuration.GetSection(ScenarioOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<ChargeProcessor>();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapGet("/", () => Results.Ok(new { service = "FakePaymentProvider", status = "up" }));

app.MapPost("/api/charges", async (
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        ChargeRequest request,
        ChargeProcessor processor,
        CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.Problem("The Idempotency-Key header is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Amount <= 0 || string.IsNullOrWhiteSpace(request.Currency) || request.Currency.Length != 3)
        {
            return Results.Problem("Amount must be positive and currency must be a 3-letter code.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await processor.ProcessAsync(idempotencyKey, request, cancellationToken);

        return result.Outcome switch
        {
            ChargeOutcome.Approved => Results.Ok(result.Response),
            ChargeOutcome.Declined => Results.Json(result.Response, statusCode: StatusCodes.Status402PaymentRequired),
            ChargeOutcome.KeyConflict => Results.Problem("Idempotency-Key was reused with a different request.", statusCode: StatusCodes.Status409Conflict),
            ChargeOutcome.Timeout => Results.StatusCode(StatusCodes.Status504GatewayTimeout),
            _ => Results.Problem("Provider is temporarily unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    });

await app.RunAsync();
