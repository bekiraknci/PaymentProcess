using Microsoft.OpenApi;

namespace PaymentSystem.Api.OpenApi;

internal static class SwaggerSetup
{
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddApiSwagger(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "PaymentSystem API",
                Version = "v1",
                Description = """
                    Payment processing and compliance API.

                    **Test accounts**
                    - `admin@test.com` / `Admin123!` (Admin)
                    - `user@test.com` / `User123!` (User)
                    - `user2@test.com` / `User123!` (User)

                    Call `POST /api/auth/login`, then paste the `accessToken` into **Authorize**.
                    """
            });

            options.AddSecurityDefinition(BearerScheme, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Paste the access token returned by /api/auth/login."
            });

            options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
            {
                [new OpenApiSecuritySchemeReference(BearerScheme, document)] = []
            });

            var xmlPath = Path.Combine(AppContext.BaseDirectory, $"{typeof(SwaggerSetup).Assembly.GetName().Name}.xml");
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

    public static WebApplication UseApiSwagger(this WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "PaymentSystem API v1");
            options.DocumentTitle = "PaymentSystem API";
            options.EnablePersistAuthorization();
            options.DisplayRequestDuration();
        });

        return app;
    }
}
