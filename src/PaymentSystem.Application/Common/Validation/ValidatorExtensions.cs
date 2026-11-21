using FluentValidation;
using PaymentSystem.Application.Common.Results;

namespace PaymentSystem.Application.Common.Validation;

public static class ValidatorExtensions
{
    public static async Task<Error?> ValidateToErrorAsync<T>(
        this IValidator<T> validator,
        T instance,
        CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return null;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());

        return Error.Validation(errors);
    }
}
