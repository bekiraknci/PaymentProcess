using Microsoft.AspNetCore.Mvc;
using PaymentSystem.Api.Contracts;
using PaymentSystem.Api.Security;
using PaymentSystem.Application.Authorization;
using PaymentSystem.Application.Reports;

namespace PaymentSystem.Api.Controllers;

[Route("api/reports")]
[HasPermission(Permissions.ReportsView)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
public sealed class ReportsController : ApiControllerBase
{
    /// <summary>
    /// Daily payment report: count and total amount per day, provider, status and currency.
    /// </summary>
    /// <remarks>
    /// Requires the `Reports.View` permission (Admin). Backed by the stored procedure `dbo.sp_GetDailyPaymentReport`. Days are UTC calendar days and
    /// both bounds are inclusive. The range can span at most 366 days. Amounts are never summed across currencies;
    /// `totalsByCurrency` gives one total per currency.
    ///
    /// Example: `/api/reports/daily-payments?from=2026-11-01&amp;to=2026-11-30`
    /// </remarks>
    /// <param name="from">First day of the range (UTC), `yyyy-MM-dd`.</param>
    /// <param name="to">Last day of the range (UTC, inclusive), `yyyy-MM-dd`.</param>
    /// <param name="providerId">Optional provider filter.</param>
    /// <param name="handler">Daily report use case.</param>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <response code="200">The report rows and per-currency totals.</response>
    /// <response code="400">Missing or invalid dates, `to` before `from`, or a range longer than 366 days.</response>
    /// <response code="401">The request has no valid access token.</response>
    /// <response code="403">The caller lacks the `Reports.View` permission.</response>
    /// <response code="404">The provider filter refers to an unknown provider.</response>
    [HttpGet("daily-payments")]
    [ProducesResponseType<ApiResponse<DailyPaymentReportResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> DailyPayments(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] Guid? providerId,
        [FromServices] GetDailyPaymentReportHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDailyPaymentReportQuery(from, to, providerId), cancellationToken);
        return result.IsSuccess ? Success(result.Value) : Problem(result.Error);
    }
}
