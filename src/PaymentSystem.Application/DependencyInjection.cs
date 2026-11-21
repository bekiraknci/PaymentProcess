using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Application.Auth.Login;
using PaymentSystem.Application.Providers.ListProviders;

namespace PaymentSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.InvariantCulture;
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<LoginHandler>();
        services.AddScoped<ListProvidersHandler>();

        return services;
    }
}
