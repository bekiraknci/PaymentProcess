using PaymentSystem.Domain.Users;

namespace PaymentSystem.Application.Abstractions.Security;

public sealed record AccessToken(string Token, DateTimeOffset ExpiresAt);

public interface IJwtTokenService
{
    AccessToken CreateToken(User user, IReadOnlyCollection<string> permissions);
}
