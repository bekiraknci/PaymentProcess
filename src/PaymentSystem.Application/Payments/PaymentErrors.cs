using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Common.Results;

namespace PaymentSystem.Application.Payments;

internal static class PaymentErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("payment.not_found", $"Payment '{id}' was not found.");

    public static Guid? OwnerScope(this ICurrentUser currentUser) => currentUser.IsAdmin ? null : currentUser.Id;
}
