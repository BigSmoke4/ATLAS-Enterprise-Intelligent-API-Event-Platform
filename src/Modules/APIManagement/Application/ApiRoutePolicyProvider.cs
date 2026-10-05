using Atlas.Modules.APIManagement.Domain;
using Atlas.Modules.APIManagement.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.APIManagement.Application;

/// <summary>
/// Resolves the configured route for a request path.
///
/// Performance/correctness notes (this runs on the live request pipeline for
/// rate limiting, telemetry attribution and timeout/retry lookups):
/// * Per-request memoization: the first lookup for a path in a request is
///   remembered in <c>HttpContext.Items</c>, so the rate limiter and the
///   telemetry middleware share one lookup instead of issuing two queries.
/// * Redis cache: resolved snapshots (including negative results) are cached
///   for <see cref="CacheTtl"/>; route configuration writes explicitly
///   invalidate the affected key, so a configuration change is visible
///   immediately rather than after the TTL.
/// * Path templates: an exact-path hit is an indexed seek; when that misses,
///   the organization's routes for the method are matched in memory against
///   the template (bounded by <see cref="MaxRoutesScannedPerLookup"/>) so
///   <c>/api/v1/orders/{id}</c> resolves for <c>/api/v1/orders/42</c>.
/// </summary>
public sealed class ApiRoutePolicyProvider : IRoutePolicyProvider
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private const int MaxRoutesScannedPerLookup = 500;

    private readonly ApiManagementDbContext _db;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ICacheService? _cache;

    public ApiRoutePolicyProvider(ApiManagementDbContext db, IHttpContextAccessor? httpContextAccessor = null, ICacheService? cache = null)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
    }

    public static string CacheKey(Guid organizationId, string method, string path)
        => $"atlas:route-policy:{organizationId:N}:{method.ToUpperInvariant()}:{path}";

    public async Task<RoutePolicySnapshot?> FindAsync(Guid? organizationId, string path, string method, CancellationToken ct = default)
    {
        if (!organizationId.HasValue || organizationId == Guid.Empty) return null;

        var normalizedMethod = string.IsNullOrWhiteSpace(method) ? "GET" : method.ToUpperInvariant();
        var requestKey = CacheKey(organizationId.Value, normalizedMethod, path);

        var items = _httpContextAccessor?.HttpContext?.Items;
        if (items is not null && items.TryGetValue(requestKey, out var memoized) && memoized is RoutePolicyCacheEntry perRequest)
        {
            return perRequest.Snapshot;
        }

        var entry = await ResolveAsync(organizationId.Value, path, normalizedMethod, ct);

        if (_cache is not null)
        {
            await _cache.SetAsync(requestKey, entry, CacheTtl, ct);
        }

        if (items is not null) items[requestKey] = entry;
        return entry.Snapshot;
    }

    private async Task<RoutePolicyCacheEntry> ResolveAsync(Guid organizationId, string path, string method, CancellationToken ct)
    {
        if (_cache is not null)
        {
            var cached = await _cache.GetAsync<RoutePolicyCacheEntry>(CacheKey(organizationId, method, path), ct);
            if (cached is not null) return cached;
        }

        var exact = await _db.ApiRoutes.AsNoTracking()
            .Where(route => route.OrganizationId == organizationId && route.Path == path && route.HttpMethod == method)
            .Select(route => new { route.Id, route.ApiVersionId, route.Path, route.HttpMethod, route.RateLimit, route.Timeout, route.MaxRetries, route.TargetServiceId })
            .FirstOrDefaultAsync(ct);

        if (exact is not null)
        {
            return new RoutePolicyCacheEntry(ToSnapshot(exact.Id, exact.ApiVersionId, exact.Path, exact.HttpMethod, exact.RateLimit, exact.Timeout, exact.MaxRetries, exact.TargetServiceId));
        }

        // Template fallback (e.g. /api/v1/orders/{id} for a request to /api/v1/orders/42).
        var candidates = await _db.ApiRoutes.AsNoTracking()
            .Where(route => route.OrganizationId == organizationId && route.HttpMethod == method)
            .OrderBy(route => route.Path)
            .Take(MaxRoutesScannedPerLookup)
            .Select(route => new { route.Id, route.ApiVersionId, route.Path, route.HttpMethod, route.RateLimit, route.Timeout, route.MaxRetries, route.TargetServiceId })
            .ToListAsync(ct);

        var matched = candidates.FirstOrDefault(candidate => RoutePatternMatcher.IsMatch(candidate.Path, path));
        if (matched is null) return new RoutePolicyCacheEntry(null);

        return new RoutePolicyCacheEntry(ToSnapshot(matched.Id, matched.ApiVersionId, matched.Path, matched.HttpMethod, matched.RateLimit, matched.Timeout, matched.MaxRetries, matched.TargetServiceId));
    }

    private static RoutePolicySnapshot ToSnapshot(Guid routeId, Guid apiVersionId, string path, string method,
        RateLimitPolicy? rateLimit, TimeSpan timeout, int maxRetries, Guid? targetServiceId)
        => new(
            rateLimit?.LimitPerWindow,
            rateLimit?.Window,
            rateLimit?.Scope.ToString(),
            rateLimit?.Algorithm.ToString(),
            ServiceId: targetServiceId,
            ApiVersionId: apiVersionId,
            Timeout: timeout,
            MaxRetries: maxRetries);
}

/// <summary>
/// Wrapper so a cached "no route configured" answer is distinguishable from a
/// cache miss (a bare null could mean either).
/// </summary>
public sealed record RoutePolicyCacheEntry(RoutePolicySnapshot? Snapshot);
