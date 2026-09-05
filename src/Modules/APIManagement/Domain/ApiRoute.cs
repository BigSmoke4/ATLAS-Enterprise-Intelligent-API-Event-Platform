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

    private ApiRoute() { }

    public static ApiRoute Create(Guid organizationId, Guid apiVersionId, string path, string httpMethod, RateLimitPolicy? rateLimit)
    {
        if (string.IsNullOrWhiteSpace(path) || !path.StartsWith('/'))
            throw new ArgumentException("Route path must start with '/'.", nameof(path));

        var method = httpMethod.ToUpperInvariant();
        if (!ValidMethods.Contains(method))
            throw new ArgumentException($"HTTP method must be one of: {string.Join(", ", ValidMethods)}.", nameof(httpMethod));

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
        if (maxRetries < 0) throw new ArgumentOutOfRangeException(nameof(maxRetries));
        MaxRetries = maxRetries;
    }
}

/// <summary>
/// Configuration value object consumed by Reliability's rate limiter
/// algorithms (Modules/Reliability/Domain) — this module only stores the
/// numbers, Reliability owns the enforcement math.
/// </summary>
public record RateLimitPolicy(int LimitPerWindow, TimeSpan Window, RateLimitScope Scope);

public enum RateLimitScope { Ip, User, ApiKey, Tenant, Endpoint, Global }
