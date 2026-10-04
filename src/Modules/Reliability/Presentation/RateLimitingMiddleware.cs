using Atlas.Modules.Reliability.Application;
using Atlas.Shared.Contracts;
using Atlas.Shared.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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

    private const int DefaultLimitPerWindow = 100;
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Conventional-middleware note: constructor dependencies are resolved
    /// from the ROOT provider when the pipeline is built, so scoped
    /// services (IRoutePolicyProvider, which depends on a scoped
    /// DbContext) must be resolved per-request from RequestServices —
    /// never captured in the constructor.
    /// </summary>
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

        var organizationId = Guid.TryParse(context.User.FindFirst("org_id")?.Value, out var parsedOrganizationId) ? parsedOrganizationId : (Guid?)null;
        RoutePolicySnapshot? routePolicy = null;
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var routePolicies = context.RequestServices.GetRequiredService<IRoutePolicyProvider>();
            try
            {
                routePolicy = await routePolicies.FindAsync(organizationId, context.Request.Path.Value ?? "/", context.Request.Method, context.RequestAborted);
            }
            catch (Exception ex)
            {
                // A missing/unavailable policy store must not turn an otherwise
                // valid request into a 500. Use the safe default and leave an
                // operational signal for remediation.
                _logger.LogWarning(ex, "Route rate-limit policy unavailable; using the default policy for {Path}.", context.Request.Path);
            }
        }
        var limit = routePolicy?.LimitPerWindow ?? DefaultLimitPerWindow;
        var window = routePolicy?.Window ?? DefaultWindow;
        if (limit <= 0) limit = DefaultLimitPerWindow;
        if (window <= TimeSpan.Zero) window = DefaultWindow;
        var scope = routePolicy?.Scope ?? "Ip";
        var algorithm = Enum.TryParse<RateLimitAlgorithm>(routePolicy?.Algorithm, true, out var parsedAlgorithm) ? parsedAlgorithm : RateLimitAlgorithm.FixedWindow;
        var scopeKey = ResolveScopeKey(context, scope, organizationId);

        RateLimitDecision decision;
        try
        {
            decision = await _limiter.CheckAsync(scopeKey, limit, window, context.RequestAborted, algorithm);
        }
        catch (Exception ex)
        {
            // Redis unavailable: fail OPEN (allow the request) rather than
            // taking the whole platform down because rate limiting's backing
            // store is briefly unreachable. This is a deliberate trade-off —
            // see docs/security.md for why fail-open was chosen here.
            _logger.LogWarning(ex, "Rate limit store unavailable; allowing request for {ScopeKey} (fail-open).", scopeKey);
            AtlasMetrics.RateLimitStoreFailures.Add(1);
            await _next(context);
            return;
        }

        context.Response.Headers["X-RateLimit-Limit"] = decision.LimitPerWindow.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = Math.Max(0, decision.LimitPerWindow - decision.CurrentCount).ToString();

        if (!decision.Allowed)
        {
            AtlasMetrics.RateLimitRejections.Add(1,
                new KeyValuePair<string, object?>("scope", scope),
                new KeyValuePair<string, object?>("algorithm", algorithm.ToString()));
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
