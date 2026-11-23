using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using PaymentSystem.Api.Contracts;
using PaymentSystem.Api.RateLimiting;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Auth.Login;
using PaymentSystem.Application.Authorization;

namespace PaymentSystem.Api.Controllers;

[Route("api/auth")]
public sealed class AuthController : ApiControllerBase
{
    /// <summary>
    /// Authenticates a user with email and password and issues a JWT access token.
    /// </summary>
    /// <remarks>
    /// Seeded accounts: `admin@test.com / Admin123!`, `user@test.com / User123!`, `user2@test.com / User123!`.
    /// In Swagger, a successful login authorizes the UI automatically with the returned `accessToken`.
    /// </remarks>
    /// <response code="200">Credentials are valid; the access token is returned.</response>
    /// <response code="400">Email or password is missing or malformed.</response>
    /// <response code="401">Email or password is incorrect.</response>
    /// <response code="429">Too many login attempts from this client; retry after the `Retry-After` seconds.</response>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests, "application/problem+json")]
    [Consumes("application/json")]
    [ProducesResponseType<ApiResponse<LoginResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        [FromServices] LoginHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password), cancellationToken);
        return result.IsSuccess ? Success(result.Value) : Problem(result.Error);
    }

    /// <summary>
    /// Returns the identity and permissions of the authenticated caller.
    /// </summary>
    /// <response code="200">The caller's identity.</response>
    /// <response code="401">The request has no valid access token.</response>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType<ApiResponse<CurrentUserResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public IActionResult Me([FromServices] ICurrentUser currentUser) =>
        Success(new CurrentUserResponse(
            currentUser.Id,
            currentUser.Email,
            currentUser.IsAdmin,
            Permissions.All.Where(currentUser.HasPermission).ToList()));
}

public sealed record CurrentUserResponse(Guid Id, string? Email, bool IsAdmin, IReadOnlyList<string> Permissions);
