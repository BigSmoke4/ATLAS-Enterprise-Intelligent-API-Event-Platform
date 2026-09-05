using Atlas.Modules.Reliability.Application;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.Reliability.Presentation;

/// <summary>
/// REAL enforcement on the live request pipeline — this is the piece that
/// was previously missing: the algorithms existed, nothing called them on
/// an actual HTTP request. Scope key is API key if present, else client IP,
/// matching the master prompt's "IP / user / API key / tenant / endpoint /
/// global" scopes (IP and API key implemented; user/tenant/endpoint scoping
/// requires reading the authenticated principal, added once route-level
/// policy lookup — APIManagement's ApiRoute.RateLimit — is wired in here).
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRequestRateLimiter _limiter;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    // TODO: replace this fixed default with a per-route lookup from
    // APIManagement.Domain.ApiRoute.RateLimit once that integration is built.
    private const int DefaultLimitPerWindow = 100;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    public RateLimitingMiddleware(RequestDelegate next, IRequestRateLimiter limiter, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _limiter = limiter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Health checks and static assets are exempt so local dev / container
        // orchestrators polling /health never get throttled.
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var scopeKey = ResolveScopeKey(context);

        RateLimitDecision decision;
        try
        {
            decision = await _limiter.CheckAsync(scopeKey, DefaultLimitPerWindow, DefaultWindow, context.RequestAborted);
        }
        catch (Exception ex)
        {
            // Redis unavailable: fail OPEN (allow the request) rather than
            // taking the whole platform down because rate limiting's backing
            // store is briefly unreachable. This is a deliberate trade-off —
            // see docs/security.md for why fail-open was chosen here.
            _logger.LogWarning(ex, "Rate limit store unavailable; allowing request for {ScopeKey} (fail-open).", scopeKey);
            await _next(context);
            return;
        }

        context.Response.Headers["X-RateLimit-Limit"] = decision.LimitPerWindow.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, decision.LimitPerWindow - decision.CurrentCount).ToString();

        if (!decision.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers["Retry-After"] = ((int)decision.Window.TotalSeconds).ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://atlas.internal/problems/rate-limited",
                title = "Rate limit exceeded",
                status = 429,
                scope = scopeKey
            });
            return;
        }

        await _next(context);
    }

    private static string ResolveScopeKey(HttpContext context)
    {
        var apiKeyPrefix = context.Request.Headers["X-Api-Key-Prefix"].FirstOrDefault();
        if (!string.IsNullOrEmpty(apiKeyPrefix)) return $"apikey:{apiKeyPrefix}";

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return $"ip:{ip}";
    }
}

public static class RateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseAtlasRateLimiting(this IApplicationBuilder app)
        => app.UseMiddleware<RateLimitingMiddleware>();
}
