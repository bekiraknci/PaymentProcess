using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Authorization;
using PaymentSystem.Application.Common.Results;

namespace PaymentSystem.Application.Payments;

internal static class PaymentErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("payment.not_found", $"Payment '{id}' was not found.");

    public static Guid? OwnerScope(this ICurrentUser currentUser) => currentUser.HasPermission(Permissions.PaymentsViewAll) ? null : currentUser.Id;
}
