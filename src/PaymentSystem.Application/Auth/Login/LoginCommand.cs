namespace PaymentSystem.Application.Auth.Login;

public sealed record LoginCommand(string Email, string Password);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType,
    DateTimeOffset ExpiresAt,
    string Email,
    string Role,
    IReadOnlyList<string> Permissions);
