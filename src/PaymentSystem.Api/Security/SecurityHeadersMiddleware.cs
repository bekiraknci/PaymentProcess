namespace PaymentSystem.Api.Security;

internal static class SecurityHeadersMiddleware
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Cross-Origin-Opener-Policy"] = "same-origin";

                if (context.Request.Path.StartsWithSegments("/api"))
                {
                    headers.CacheControl = "no-store";
                    headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
                }

                return Task.CompletedTask;
            });

            await next();
        });
}
