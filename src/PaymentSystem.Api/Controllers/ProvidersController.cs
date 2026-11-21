using Microsoft.AspNetCore.Mvc;
using PaymentSystem.Api.Security;
using PaymentSystem.Application.Authorization;
using PaymentSystem.Application.Providers;
using PaymentSystem.Application.Providers.ListProviders;

namespace PaymentSystem.Api.Controllers;

[Route("api/providers")]
[HasPermission(Permissions.ProvidersManage)]
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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> List([FromServices] ListProvidersHandler handler, CancellationToken cancellationToken) =>
        Ok(await handler.HandleAsync(cancellationToken));
}
