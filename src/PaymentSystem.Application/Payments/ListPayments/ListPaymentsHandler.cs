using FluentValidation;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Abstractions.Security;
using PaymentSystem.Application.Common.Paging;
using PaymentSystem.Application.Common.Results;
using PaymentSystem.Application.Common.Validation;
using PaymentSystem.Domain.Payments;

namespace PaymentSystem.Application.Payments.ListPayments;

public sealed record ListPaymentsQuery(PaymentStatus? Status, int Page = 1, int PageSize = 20);

public sealed class ListPaymentsValidator : AbstractValidator<ListPaymentsQuery>
{
    public const int MaxPageSize = 100;

    public ListPaymentsValidator()
    {
        RuleFor(x => x.Status).IsInEnum().When(x => x.Status.HasValue);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}

public sealed class ListPaymentsHandler(
    IValidator<ListPaymentsQuery> validator,
    IPaymentQueries queries,
    ICurrentUser currentUser)
{
    public async Task<Result<PagedResult<PaymentResponse>>> HandleAsync(ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        if (await validator.ValidateToErrorAsync(query, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        return await queries.ListAsync(currentUser.OwnerScope(), query.Status, query.Page, query.PageSize, cancellationToken);
    }
}
