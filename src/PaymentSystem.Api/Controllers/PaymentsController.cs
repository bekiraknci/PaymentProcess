using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PaymentSystem.Api.Contracts;
using PaymentSystem.Api.RateLimiting;
using PaymentSystem.Api.Security;
using PaymentSystem.Application.Authorization;
using PaymentSystem.Application.Common.Paging;
using PaymentSystem.Application.Payments;
using PaymentSystem.Application.Payments.CreatePayment;
using PaymentSystem.Application.Payments.GetPayment;
using PaymentSystem.Application.Payments.ListPayments;
using PaymentSystem.Application.Payments.RetryPayment;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Api.Controllers;

[Route("api/payments")]
public sealed class PaymentsController : ApiControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";
    public const string IdempotentReplayedHeader = "Idempotent-Replayed";

    /// <summary>
    /// Submits a new payment. The payment is stored as `Pending` and processed asynchronously.
    /// </summary>
    /// <remarks>
    /// Send an `Idempotency-Key` header to make the request safe to retry. Repeating the same key with the
    /// same body returns the original payment with the `Idempotent-Replayed: true` header. Repeating it with a
    /// different body returns 409.
    ///
    /// Deterministic provider outcomes for testing: amount `666` is always declined, `503` always hits a
    /// provider outage, `999` always times out.
    /// </remarks>
    /// <param name="request">Provider, amount and ISO 4217 currency.</param>
    /// <param name="idempotencyKey">Client-generated unique key (max 128 printable ASCII characters).</param>
    /// <param name="handler">Create payment use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="201">The payment was accepted (or replayed for a repeated Idempotency-Key).</response>
    /// <response code="400">The request is invalid, or the provider is inactive or does not support the currency.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Payments.Create` permission.</response>
    /// <response code="404">The provider does not exist.</response>
    /// <response code="409">The Idempotency-Key was already used with a different request body.</response>
    /// <response code="429">The caller exceeded the payment submission rate limit.</response>
    [HttpPost]
    [HasPermission(Permissions.PaymentsCreate)]
    [EnableRateLimiting(RateLimitPolicies.PaymentCreate)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [Consumes("application/json")]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(
        [FromBody] CreatePaymentRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        [FromServices] CreatePaymentHandler handler,
        CancellationToken cancellationToken)
    {
        var command = new CreatePaymentCommand(request.ProviderId, request.Amount, request.Currency, idempotencyKey);
        var result = await handler.HandleAsync(command, cancellationToken);
        if (!result.IsSuccess)
        {
            return Problem(result.Error);
        }

        if (result.Value.Replayed)
        {
            Response.Headers[IdempotentReplayedHeader] = "true";
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value.Payment.Id }, result.Value.Payment);
    }

    /// <summary>
    /// Returns a single payment.
    /// </summary>
    /// <remarks>Users see only their own payments; administrators see all payments.</remarks>
    /// <param name="id">Payment identifier.</param>
    /// <param name="handler">Get payment use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">The payment.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Payments.View` permission.</response>
    /// <response code="404">The payment does not exist or belongs to another user.</response>
    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetById(Guid id, [FromServices] GetPaymentByIdHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Lists payments, newest first, with optional status filter and paging.
    /// </summary>
    /// <remarks>Users see only their own payments; administrators see all payments.</remarks>
    /// <param name="status">Optional status filter: `Pending`, `Processing`, `Completed` or `Failed`.</param>
    /// <param name="page">Page number, starting at 1.</param>
    /// <param name="pageSize">Items per page (1-100).</param>
    /// <param name="handler">List payments use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">A page of payments.</response>
    /// <response code="400">Paging or filter parameters are invalid.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Payments.View` permission.</response>
    [HttpGet]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType<PagedResult<PaymentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> List(
        [FromQuery] PaymentStatus? status,
        [FromServices] ListPaymentsHandler handler,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await handler.HandleAsync(new ListPaymentsQuery(status, page, pageSize), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Returns the status history of a payment in chronological order.
    /// </summary>
    /// <param name="id">Payment identifier.</param>
    /// <param name="handler">Payment history use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">The status transitions of the payment.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Payments.View` permission.</response>
    /// <response code="404">The payment does not exist or belongs to another user.</response>
    [HttpGet("{id:guid}/history")]
    [HasPermission(Permissions.PaymentsView)]
    [ProducesResponseType<IReadOnlyList<PaymentHistoryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetHistory(Guid id, [FromServices] GetPaymentHistoryHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Requeues a failed payment for another processing attempt.
    /// </summary>
    /// <remarks>
    /// Requires the `Payments.Process` permission (Admin). A manual retry is an administrator override and is
    /// not limited by the automatic retry attempt cap.
    /// </remarks>
    /// <param name="id">Payment identifier.</param>
    /// <param name="handler">Retry payment use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="202">The payment was requeued and will be processed asynchronously.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Payments.Process` permission.</response>
    /// <response code="404">The payment does not exist.</response>
    /// <response code="409">The payment is not in the `Failed` status.</response>
    [HttpPost("{id:guid}/retry")]
    [HasPermission(Permissions.PaymentsProcess)]
    [ProducesResponseType<PaymentResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Retry(Guid id, [FromServices] RetryPaymentHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess
            ? AcceptedAtAction(nameof(GetById), new { id }, result.Value)
            : Problem(result.Error);
    }
}
