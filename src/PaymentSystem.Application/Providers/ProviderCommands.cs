using FluentValidation;
using PaymentSystem.Domain.Payments;
using PaymentSystem.Domain.Providers;

namespace PaymentSystem.Application.Providers;

public sealed record CreateProviderCommand(
    string Code,
    string Name,
    string? BaseUrl,
    IReadOnlyList<string> SupportedCurrencies,
    bool IsActive = true);

public sealed record UpdateProviderCommand(
    string Name,
    string? BaseUrl,
    IReadOnlyList<string> SupportedCurrencies);

public sealed class CreateProviderValidator : AbstractValidator<CreateProviderCommand>
{
    public CreateProviderValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(PaymentProvider.CodeMaxLength)
            .Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Code may contain only letters, digits, '_' and '-'.");

        RuleFor(x => x.Name).ProviderName();
        RuleFor(x => x.BaseUrl).ProviderBaseUrl();
        RuleFor(x => x.SupportedCurrencies).ProviderCurrencies();
    }
}

public sealed class UpdateProviderValidator : AbstractValidator<UpdateProviderCommand>
{
    public UpdateProviderValidator()
    {
        RuleFor(x => x.Name).ProviderName();
        RuleFor(x => x.BaseUrl).ProviderBaseUrl();
        RuleFor(x => x.SupportedCurrencies).ProviderCurrencies();
    }
}

internal static class ProviderRules
{
    public static IRuleBuilderOptions<T, string> ProviderName<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().MaximumLength(PaymentProvider.NameMaxLength);

    public static IRuleBuilderOptions<T, string?> ProviderBaseUrl<T>(this IRuleBuilder<T, string?> rule) =>
        rule.MaximumLength(PaymentProvider.BaseUrlMaxLength)
            .Must(url => string.IsNullOrEmpty(url) ||
                         (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)))
            .WithMessage("Base URL must be an absolute http(s) URL.");

    public static IRuleBuilderOptions<T, IReadOnlyList<string>> ProviderCurrencies<T>(this IRuleBuilder<T, IReadOnlyList<string>> rule) =>
        rule.NotEmpty()
            .Must(currencies => currencies.All(Currency.IsSupported))
            .WithMessage($"Supported currencies must be a subset of: {string.Join(", ", Currency.Supported.Order())}.");
}
