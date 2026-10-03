using Atlas.Modules.Reliability.Application;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.Reliability.Presentation;

/// <summary>
/// REAL enforcement on the live request pipeline — this is the piece that
/// was previously missing: the algorithms existed, nothing called them on
/// an actual HTTP request. Scope key is API key if present, else client IP,
/// matching the master prompt's "IP / user / API key / tenant / endpoint /
/// global" scopes. Route policies come from the APIManagement application
/// contract; an explicit default remains for unconfigured routes.
/// </summary>
public class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRequestRateLimiter _limiter;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly IRoutePolicyProvider _routePolicies;

    private const int DefaultLimitPerWindow = 100;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    public RateLimitingMiddleware(RequestDelegate next, IRequestRateLimiter limiter, ILogger<RateLimitingMiddleware> logger, IRoutePolicyProvider routePolicies)
    {
        _next = next;
        _limiter = limiter;
        _logger = logger;
        _routePolicies = routePolicies;
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

        var organizationId = Guid.TryParse(context.User.FindFirst("org_id")?.Value, out var parsedOrganizationId) ? parsedOrganizationId : (Guid?)null;
        var routePolicy = await _routePolicies.FindAsync(organizationId, context.Request.Path.Value ?? "/", context.Request.Method, context.RequestAborted);
        var limit = routePolicy?.LimitPerWindow ?? DefaultLimitPerWindow;
        var window = routePolicy?.Window ?? DefaultWindow;
        var scope = routePolicy?.Scope ?? "Ip";
        var scopeKey = ResolveScopeKey(context, scope, organizationId);

        RateLimitDecision decision;
        try
        {
            decision = await _limiter.CheckAsync(scopeKey, limit, window, context.RequestAborted);
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

    private static string ResolveScopeKey(HttpContext context, string scope, Guid? organizationId)
    {
        var normalized = scope.ToLowerInvariant();
        if (normalized == "global") return "global";
        if (normalized == "tenant") return $"tenant:{organizationId?.ToString() ?? "anonymous"}";
        if (normalized == "user") return $"user:{context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anonymous"}";
        if (normalized == "endpoint") return $"endpoint:{context.Request.Method}:{context.Request.Path}";
        if (normalized == "apikey") return $"apikey:{context.Request.Headers["X-Api-Key-Prefix"].FirstOrDefault() ?? "anonymous"}";
        return $"ip:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
    }
}

public static class RateLimitingMiddlewareExtensions
{
    public static IApplicationBuilder UseAtlasRateLimiting(this IApplicationBuilder app)
        => app.UseMiddleware<RateLimitingMiddleware>();
}
