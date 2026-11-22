using Microsoft.AspNetCore.Mvc;
using PaymentSystem.Api.Contracts;
using PaymentSystem.Api.Security;
using PaymentSystem.Application.Authorization;
using PaymentSystem.Application.Providers;
using PaymentSystem.Application.Providers.ListProviders;

namespace PaymentSystem.Api.Controllers;

[Route("api/providers")]
[HasPermission(Permissions.ProvidersManage)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ProvidersController : ApiControllerBase
{
    /// <summary>
    /// Lists all payment providers, active and inactive.
    /// </summary>
    /// <remarks>Requires the `Providers.Manage` permission (Admin).</remarks>
    /// <response code="200">The list of providers ordered by code.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Providers.Manage` permission.</response>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ProviderResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromServices] ListProvidersHandler handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(cancellationToken));

    /// <summary>
    /// Returns a single payment provider.
    /// </summary>
    /// <param name="id">Provider identifier.</param>
    /// <param name="handler">Get provider use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">The provider.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Providers.Manage` permission.</response>
    /// <response code="404">The provider does not exist.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<ProviderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetById(Guid id, [FromServices] GetProviderHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Registers a new payment provider.
    /// </summary>
    /// <remarks>The code is normalized to upper case and must be unique.</remarks>
    /// <param name="request">Provider code, name, optional base URL, supported currencies and initial state.</param>
    /// <param name="handler">Create provider use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="201">The provider was created.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Providers.Manage` permission.</response>
    /// <response code="409">A provider with the same code already exists.</response>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ProviderResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Create(
        [FromBody] CreateProviderRequest request,
        [FromServices] CreateProviderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateProviderCommand(request.Code, request.Name, request.BaseUrl, request.SupportedCurrencies, request.IsActive), cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : Problem(result.Error);
    }

    /// <summary>
    /// Updates the name, base URL and supported currencies of a provider.
    /// </summary>
    /// <param name="id">Provider identifier.</param>
    /// <param name="request">New provider details.</param>
    /// <param name="handler">Update provider use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">The updated provider.</response>
    /// <response code="400">The request is invalid.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Providers.Manage` permission.</response>
    /// <response code="404">The provider does not exist.</response>
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<ProviderResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] UpdateProviderRequest request,
        [FromServices] UpdateProviderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, new UpdateProviderCommand(request.Name, request.BaseUrl, request.SupportedCurrencies), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Activates a provider so it can accept new payments.
    /// </summary>
    /// <param name="id">Provider identifier.</param>
    /// <param name="handler">Provider status use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="204">The provider is active.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Providers.Manage` permission.</response>
    /// <response code="404">The provider does not exist.</response>
    [HttpPatch("{id:guid}/activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Activate(Guid id, [FromServices] SetProviderStatusHandler handler, CancellationToken cancellationToken) =>
        SetStatus(id, active: true, handler, cancellationToken);

    /// <summary>
    /// Deactivates a provider; new payments to it are rejected with 400.
    /// </summary>
    /// <param name="id">Provider identifier.</param>
    /// <param name="handler">Provider status use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="204">The provider is inactive.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Providers.Manage` permission.</response>
    /// <response code="404">The provider does not exist.</response>
    [HttpPatch("{id:guid}/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public Task<IActionResult> Deactivate(Guid id, [FromServices] SetProviderStatusHandler handler, CancellationToken cancellationToken) =>
        SetStatus(id, active: false, handler, cancellationToken);

    private async Task<IActionResult> SetStatus(Guid id, bool active, SetProviderStatusHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, active, cancellationToken);
        return result.IsSuccess ? NoContent() : Problem(result.Error);
    }
}
