namespace Morita.LP.Razor.Configuration;

public static class SecurityHeaders
{
    public const string ContentSecurityPolicy = "frame-ancestors 'self'; base-uri 'self'; object-src 'none'";
    public const string ReferrerPolicy = "strict-origin-when-cross-origin";
    public const string PermissionsPolicy = "camera=(), microphone=(), geolocation=()";

    // Applies to every response, including static files and error pages. The policy only limits who may
    // frame the storefront and where <base> and plugins may point; script sources stay open because the
    // tag manager and the payment SDK load their own.
    public static IApplicationBuilder UseStorefrontSecurityHeaders(this IApplicationBuilder app) =>
        app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "SAMEORIGIN";
                headers["Referrer-Policy"] = ReferrerPolicy;
                headers["Permissions-Policy"] = PermissionsPolicy;
                headers.ContentSecurityPolicy = ContentSecurityPolicy;
                return Task.CompletedTask;
            });
            return next(context);
        });
}
