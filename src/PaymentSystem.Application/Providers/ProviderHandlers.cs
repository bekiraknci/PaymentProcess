using FluentValidation;
using PaymentSystem.Application.Abstractions.Persistence;
using PaymentSystem.Application.Common.Results;
using PaymentSystem.Application.Common.Validation;
using PaymentSystem.Domain.Providers;

namespace PaymentSystem.Application.Providers;

internal static class ProviderErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("provider.not_found", $"Payment provider '{id}' was not found.");

    public static Error CodeTaken(string code) =>
        Error.Conflict("provider.code_taken", $"A payment provider with code '{code.Trim().ToUpperInvariant()}' already exists.");
}

public sealed class GetProviderHandler(IPaymentProviderRepository providers)
{
    public async Task<Result<ProviderResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var provider = await providers.GetByIdAsync(id, cancellationToken);
        return provider is null ? ProviderErrors.NotFound(id) : ProviderResponse.From(provider);
    }
}

public sealed class CreateProviderHandler(
    IValidator<CreateProviderCommand> validator,
    IPaymentProviderRepository providers,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
{
    public async Task<Result<ProviderResponse>> HandleAsync(CreateProviderCommand command, CancellationToken cancellationToken)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        if (await providers.CodeExistsAsync(command.Code, cancellationToken))
        {
            return ProviderErrors.CodeTaken(command.Code);
        }

        var provider = PaymentProvider.Create(
            command.Code,
            command.Name,
            command.BaseUrl,
            command.SupportedCurrencies,
            command.IsActive,
            timeProvider.GetUtcNow());

        providers.Add(provider);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return ProviderErrors.CodeTaken(command.Code);
        }

        return ProviderResponse.From(provider);
    }
}

public sealed class UpdateProviderHandler(
    IValidator<UpdateProviderCommand> validator,
    IPaymentProviderRepository providers,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<ProviderResponse>> HandleAsync(Guid id, UpdateProviderCommand command, CancellationToken cancellationToken)
    {
        if (await validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return validationError;
        }

        var provider = await providers.GetByIdAsync(id, cancellationToken);
        if (provider is null)
        {
            return ProviderErrors.NotFound(id);
        }

        provider.Update(command.Name, command.BaseUrl, command.SupportedCurrencies);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ProviderResponse.From(provider);
    }
}

public sealed class SetProviderStatusHandler(IPaymentProviderRepository providers, IUnitOfWork unitOfWork)
{
    public async Task<Result> HandleAsync(Guid id, bool active, CancellationToken cancellationToken)
    {
        var provider = await providers.GetByIdAsync(id, cancellationToken);
        if (provider is null)
        {
            return ProviderErrors.NotFound(id);
        }

        if (active)
        {
            provider.Activate();
        }
        else
        {
            provider.Deactivate();
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
