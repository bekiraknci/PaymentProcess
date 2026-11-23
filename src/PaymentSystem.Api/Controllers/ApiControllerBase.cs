using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using PaymentSystem.Api.Contracts;
using PaymentSystem.Application.Common.Results;

namespace PaymentSystem.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected static OkObjectResult Success<T>(T data) =>
        new(Wrap(StatusCodes.Status200OK, data));

    protected CreatedAtActionResult SuccessCreated<T>(string actionName, object routeValues, T data) =>
        CreatedAtAction(actionName, routeValues, Wrap(StatusCodes.Status201Created, data));

    protected AcceptedAtActionResult SuccessAccepted<T>(string actionName, object routeValues, T data) =>
        AcceptedAtAction(actionName, routeValues, Wrap(StatusCodes.Status202Accepted, data));

    private static ApiResponse<T> Wrap<T>(int statusCode, T data) => new(statusCode, true, data);

    protected ObjectResult Problem(Error error)
    {
        if (error is { Type: ErrorType.Validation, ValidationErrors: { } errors })
        {
            var modelState = new ModelStateDictionary();
            foreach (var (field, messages) in errors)
            {
                foreach (var message in messages)
                {
                    modelState.AddModelError(field, message);
                }
            }

            var validation = ProblemDetailsFactory.CreateValidationProblemDetails(
                HttpContext, modelState, StatusCodes.Status400BadRequest, error.Message);
            validation.Extensions["code"] = error.Code;
            return AsProblemResult(validation);
        }

        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError
        };

        var problem = ProblemDetailsFactory.CreateProblemDetails(HttpContext, status, detail: error.Message);
        problem.Extensions["code"] = error.Code;
        return AsProblemResult(problem);
    }

    private static ObjectResult AsProblemResult(ProblemDetails problem) => new(problem)
    {
        StatusCode = problem.Status,
        ContentTypes = { "application/problem+json" }
    };
}
