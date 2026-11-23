using FluentValidation;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Common.Results;
using PaymentSystem.Application.Common.Validation;

namespace PaymentSystem.Application.Auth.Login;

public sealed class LoginHandler(
    IValidator<LoginCommand> validator,
    IUserRepository users,
    IPasswordHasher passwordHasher,
    IPermissionRepository permissions,
    IJwtTokenService tokenService)
{
    private static readonly Error InvalidCredentials =
        Error.Unauthorized("auth.invalid_credentials", "Email or password is incorrect.");

    private static string? _dummyHash;

    public async Task<Result<LoginResponse>> HandleAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        var email = command.Email.Trim().ToLowerInvariant();
        var user = await users.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            _dummyHash ??= passwordHasher.Hash(Guid.NewGuid().ToString());
            passwordHasher.Verify(_dummyHash, command.Password);
            return InvalidCredentials;
        }

        if (!passwordHasher.Verify(user.PasswordHash, command.Password))
        {
            return InvalidCredentials;
        }

        var granted = await permissions.GetForRoleAsync(user.Role, cancellationToken);
        var token = tokenService.CreateToken(user, granted);

        return new LoginResponse(
            token.Token,
            "Bearer",
            token.ExpiresAt,
            user.Email,
            user.Role.ToString(),
            granted);
    }
}
