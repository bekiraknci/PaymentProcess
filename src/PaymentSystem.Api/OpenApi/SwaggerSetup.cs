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

                    A successful `POST /api/auth/login` authorizes Swagger automatically with the returned token.
                    To switch users, log in again; to test 401, use **Authorize → Logout**.
                    When pasting a token manually, the `Bearer ` prefix is optional.

                    **Deterministic provider outcomes** (start `FakePaymentProvider.Api` on http://localhost:5100)
                    - amount `666` → always declined
                    - amount `503` → provider outage (503)
                    - amount `999` → provider timeout
                    - amount `502` → fails the first attempt, then the retry job completes it
                    - any other amount → mostly approved, with configurable random failures

                    Seeded provider `FAKEPAY` id: `0196a000-0000-7000-8000-000000000001` (active, EUR/GBP/TRY/USD).
                    Seeded provider `LEGACYPAY` id: `0196a000-0000-7000-8000-000000000002` (inactive).
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
            options.UseResponseInterceptor("""
                (res) => {
                    if (res.ok && res.url.endsWith('/api/auth/login')) {
                        try {
                            const token = JSON.parse(res.text).accessToken;
                            if (token) { window.ui.preauthorizeApiKey('Bearer', token); }
                        } catch { }
                    }
                    return res;
                }
                """);
            options.DisplayRequestDuration();
        });

        return app;
    }
}
