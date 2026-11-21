using System.Globalization;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Application.Auth.Login;
using PaymentSystem.Application.Payments.CreatePayment;
using PaymentSystem.Application.Payments.GetPayment;
using PaymentSystem.Application.Payments.ListPayments;
using PaymentSystem.Application.Payments.Processing;
using PaymentSystem.Application.Payments.RetryPayment;
using PaymentSystem.Application.Providers;
using PaymentSystem.Application.Providers.ListProviders;

namespace PaymentSystem.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ValidatorOptions.Global.LanguageManager.Culture = CultureInfo.InvariantCulture;
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly, includeInternalTypes: true);

        services.AddScoped<LoginHandler>();

        services.AddScoped<CreatePaymentHandler>();
        services.AddScoped<GetPaymentByIdHandler>();
        services.AddScoped<GetPaymentHistoryHandler>();
        services.AddScoped<ListPaymentsHandler>();
        services.AddScoped<RetryPaymentHandler>();
        services.AddScoped<ProcessPaymentHandler>();
        services.AddScoped<RecoverPaymentsHandler>();

        services.AddScoped<ListProvidersHandler>();
        services.AddScoped<GetProviderHandler>();
        services.AddScoped<CreateProviderHandler>();
        services.AddScoped<UpdateProviderHandler>();
        services.AddScoped<SetProviderStatusHandler>();

        return services;
    }
}
