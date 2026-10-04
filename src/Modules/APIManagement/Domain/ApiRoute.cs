using Atlas.Shared.Domain;

namespace Atlas.Modules.APIManagement.Domain;

/// <summary>
/// A concrete routable endpoint, e.g. GET /api/v1/orders/{id}. Carries the
/// per-route policies (rate limit, timeout, retry) that Reliability and
/// TrafficManagement consult at request time via the ApiRoutePolicy DTO —
/// this module owns configuration, it does not itself enforce it on the
/// live pipeline.
/// </summary>
public class ApiRoute : TenantEntity
{
    private static readonly string[] ValidMethods = { "GET", "POST", "PUT", "PATCH", "DELETE" };

    public Guid ApiVersionId { get; private set; }
    public string Path { get; private set; } = string.Empty;
    public string HttpMethod { get; private set; } = string.Empty;
    public RateLimitPolicy? RateLimit { get; private set; }
    public TimeSpan Timeout { get; private set; } = TimeSpan.FromSeconds(30);
    public int MaxRetries { get; private set; } = 0;

    /// <summary>
    /// The registered service (ServiceRegistry) this route dispatches to.
    /// Nullable on purpose: a route can be catalogued before its backing
    /// service is registered, and telemetry attribution treats "no target
    /// service" as unattributed rather than guessing.
    /// </summary>
    public Guid? TargetServiceId { get; private set; }

    private ApiRoute() { }

    public static ApiRoute Create(Guid organizationId, Guid apiVersionId, string path, string httpMethod, RateLimitPolicy? rateLimit, Guid? targetServiceId = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
            throw new ArgumentException("Route path must start with '/'.", nameof(path));

        var method = httpMethod.ToUpperInvariant();
        if (!ValidMethods.Contains(method))
            throw new ArgumentException($"HTTP method must be one of: {string.Join(", ", ValidMethods)}.", nameof(httpMethod));
        if (rateLimit is not null && (rateLimit.LimitPerWindow <= 0 || rateLimit.Window <= TimeSpan.Zero))
            throw new ArgumentException("Rate limit must have a positive limit and window.", nameof(rateLimit));

        return new ApiRoute
        {
            OrganizationId = organizationId,
            ApiVersionId = apiVersionId,
            Path = path,
            HttpMethod = method,
            RateLimit = rateLimit
        };
    }

    public void SetTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        Timeout = timeout;
    }

    public void SetRetryPolicy(int maxRetries)
    {
        if (maxRetries < 0 || maxRetries > 10) throw new ArgumentOutOfRangeException(nameof(maxRetries), "Retries must be between 0 and 10.");
        MaxRetries = maxRetries;
    }

    public void SetRateLimit(RateLimitPolicy? rateLimit)
    {
        if (rateLimit is not null && (rateLimit.LimitPerWindow <= 0 || rateLimit.Window <= TimeSpan.Zero))
            throw new ArgumentException("Rate limit must have a positive limit and window.");
        RateLimit = rateLimit;
    }

    /// <summary>Points the route at a registered service, or clears the target when null.</summary>
    public void SetTargetService(Guid? serviceId)
    {
        if (serviceId == Guid.Empty) serviceId = null;
        TargetServiceId = serviceId;
        Touch();
    }
}

/// <summary>
/// Configuration value object consumed by Reliability's rate limiter
/// algorithms (Modules/Reliability/Domain) — this module only stores the
/// numbers, Reliability owns the enforcement math.
/// </summary>
public record RateLimitPolicy(int LimitPerWindow, TimeSpan Window, RateLimitScope Scope, RateLimitAlgorithm Algorithm = RateLimitAlgorithm.FixedWindow)
{
    /// <summary>Window in whole seconds — the unit the console and the Redis limiter both speak.</summary>
    public int WindowSeconds => (int)Math.Round(Window.TotalSeconds);
}

public enum RateLimitScope { Ip, User, ApiKey, Tenant, Endpoint, Global }
public enum RateLimitAlgorithm { FixedWindow, TokenBucket, SlidingWindow, LeakyBucket }
