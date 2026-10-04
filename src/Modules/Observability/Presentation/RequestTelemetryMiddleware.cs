using System.Diagnostics;
using Atlas.Modules.Observability.Application;
using Atlas.Shared.Contracts;
using Atlas.Shared.Observability;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.Observability.Presentation;

/// <summary>
/// Records every HTTP request that ATLAS serves: Prometheus counters and a
/// histogram (always), plus a durable per-tenant bucket via
/// <see cref="ITelemetryIngestService"/> (when the caller is attributable to
/// an organization).
///
/// Two invariants:
/// 1. Telemetry failure never affects the request — every branch is guarded
///    and the ingest call is a non-blocking bounded-channel write.
/// 2. Route labels are normalized ({id}/{n}) so metric cardinality stays
///    bounded; raw paths with identifiers must never become labels.
/// </summary>
public sealed class RequestTelemetryMiddleware
{
    private static readonly string[] ExcludedPrefixes = { "/health", "/metrics", "/css", "/js", "/images", "/fonts", "/favicon", "/hubs" };

    private readonly RequestDelegate _next;
    private readonly ITelemetryIngestService _ingest;
    private readonly ILogger<RequestTelemetryMiddleware> _logger;

    public RequestTelemetryMiddleware(RequestDelegate next, ITelemetryIngestService ingest, ILogger<RequestTelemetryMiddleware> logger)
    {
        _next = next;
        _ingest = ingest;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsExcluded(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await _next(context);
        }
        catch
        {
            stopwatch.Stop();
            await RecordAsync(context, StatusCodes.Status500InternalServerError, stopwatch.Elapsed.TotalMilliseconds);
            throw;
        }

        stopwatch.Stop();
        await RecordAsync(context, context.Response.StatusCode, stopwatch.Elapsed.TotalMilliseconds);
    }

    private async Task RecordAsync(HttpContext context, int statusCode, double durationMs)
    {
        try
        {
            var route = ResolveRoute(context);
            var statusClass = Classify(statusCode);

            AtlasMetrics.HttpRequests.Add(1,
                new KeyValuePair<string, object?>("route", route),
                new KeyValuePair<string, object?>("method", context.Request.Method),
                new KeyValuePair<string, object?>("status_class", statusClass));

            if (statusCode >= 500)
            {
                AtlasMetrics.HttpServerErrors.Add(1, new KeyValuePair<string, object?>("route", route));
            }
            else if (statusCode >= 400)
            {
                AtlasMetrics.HttpClientErrors.Add(1, new KeyValuePair<string, object?>("route", route));
            }

            AtlasMetrics.HttpRequestDuration.Record(durationMs,
                new KeyValuePair<string, object?>("route", route),
                new KeyValuePair<string, object?>("status_class", statusClass));

            // Only organization-attributed traffic becomes a tenant row;
            // anonymous/unattributed traffic still reaches Prometheus above.
            if (!Guid.TryParse(context.User.FindFirst("org_id")?.Value, out var organizationId) || organizationId == Guid.Empty) return;

            Guid? serviceId = null;
            var path = context.Request.Path.Value ?? "/";
            if (path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                var routes = context.RequestServices.GetService<IRoutePolicyProvider>();
                if (routes is not null)
                {
                    var snapshot = await routes.FindAsync(organizationId, path, context.Request.Method, context.RequestAborted);
                    serviceId = snapshot?.ServiceId;
                }
            }

            _ingest.TryRecord(new RequestTelemetryObservation(
                organizationId, serviceId, route, context.Request.Method, statusCode, durationMs, DateTimeOffset.UtcNow, null));
        }
        catch (Exception ex)
        {
            // Never fail a request because of observability.
            _logger.LogDebug(ex, "Request telemetry recording skipped for {Path}.", context.Request.Path);
        }
    }

    private static string Classify(int statusCode) => statusCode >= 500 ? "5xx"
        : statusCode >= 400 ? "4xx"
        : statusCode >= 300 ? "3xx"
        : "2xx";

    private static bool IsExcluded(PathString path)
    {
        foreach (var prefix in ExcludedPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string ResolveRoute(HttpContext context)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is RouteEndpoint routeEndpoint && !string.IsNullOrWhiteSpace(routeEndpoint.RoutePattern.RawText))
        {
            return NormalizeRoute(routeEndpoint.RoutePattern.RawText!);
        }
        return NormalizeRoute(context.Request.Path.Value ?? "/");
    }

    /// <summary>Collapses identifiers so a route label never explodes metric cardinality.</summary>
    internal static string NormalizeRoute(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            if (Guid.TryParse(segments[i], out _)) segments[i] = "{id}";
            else if (segments[i].Length > 0 && segments[i].All(char.IsDigit)) segments[i] = "{n}";
        }

        return "/" + string.Join('/', segments);
    }
}

public static class RequestTelemetryMiddlewareExtensions
{
    public static IApplicationBuilder UseAtlasRequestTelemetry(this IApplicationBuilder app)
        => app.UseMiddleware<RequestTelemetryMiddleware>();
}
