using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Domain.Common;

namespace PaymentSystem.Api.Errors;

internal sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetailsService,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            DomainException domain when domain.Code.EndsWith("invalid_status_transition", StringComparison.Ordinal) =>
                Create(StatusCodes.Status409Conflict, "Invalid state transition", domain.Message, domain.Code),
            DomainException domain =>
                Create(StatusCodes.Status400BadRequest, "Business rule violation", domain.Message, domain.Code),
            DbUpdateConcurrencyException or ConcurrencyConflictException =>
                Create(StatusCodes.Status409Conflict, "Concurrency conflict", "The resource was modified by another request. Please retry.", "concurrency_conflict"),
            UniqueConstraintViolationException =>
                Create(StatusCodes.Status409Conflict, "Conflict", "The resource conflicts with an existing one.", "unique_violation"),
            BadHttpRequestException bad =>
                Create(bad.StatusCode, "Bad request", bad.Message, "bad_request"),
            OperationCanceledException when httpContext.RequestAborted.IsCancellationRequested =>
                Create(499, "Client closed request", null, "request_cancelled"),
            _ =>
                Create(StatusCodes.Status500InternalServerError, "An unexpected error occurred", null, "internal_error")
        };

        if (problem.Status >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception for {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "Request failed with {StatusCode}: {Message}", problem.Status, exception.Message);
        }

        httpContext.Response.StatusCode = problem.Status!.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails Create(int status, string title, string? detail, string code) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Extensions = { ["code"] = code }
    };
}
