using Microsoft.AspNetCore.Http;

namespace Atlas.Shared.Web;

/// <summary>
/// Real security headers applied to every response — not a config comment,
/// actual middleware in the pipeline. Baseline hardening per
/// docs/security.md / docs/threat-model.md: clickjacking, MIME-sniffing,
/// referrer leakage, and a conservative default CSP for the server-rendered
/// Razor pages (script-src 'self' only; no inline scripts are used anywhere
/// in wwwroot/js per the "centralized JavaScript" rule, so this CSP doesn't
/// need 'unsafe-inline').
/// </summary>
public class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            headers["Permissions-Policy"] = "geolocation=(), microphone=(), camera=()";
            headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; frame-ancestors 'none'";
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public static class SecurityHeadersMiddlewareExtensions
{
    public static IApplicationBuilder UseAtlasSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
