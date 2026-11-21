namespace PaymentSystem.Application.Common.Results;

public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record Error(string Code, string Message, ErrorType Type)
{
    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; init; }

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Validation(IReadOnlyDictionary<string, string[]> errors) =>
        new("validation_failed", "One or more validation errors occurred.", ErrorType.Validation)
        {
            ValidationErrors = errors
        };

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);
}
