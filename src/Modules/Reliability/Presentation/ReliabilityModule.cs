using Atlas.Modules.Reliability.Application;
using Atlas.Modules.Reliability.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Atlas.Modules.Reliability.Presentation;

/// <summary>
/// STATUS: real end-to-end. RedisRateLimitStore backs the distributed
/// counter; FixedWindowRequestRateLimiter + RateLimitingMiddleware enforce
/// it on every live HTTP request (see Program.cs `app.UseAtlasRateLimiting()`);
/// CircuitBreakerRegistry + CircuitBreakerDelegatingHandler enforce a real
/// circuit breaker on outbound HttpClient calls attached to it. Rate limits
/// use the route policy provider when a matching API route is configured;
/// otherwise they fall back to a documented 100 req/min IP limit. Scope
/// keys support IP, user, API key, tenant, endpoint, and global policies.
/// </summary>
public class ReliabilityModule : IAtlasModule
{
    public string Name => "Reliability";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis is not configured.");

        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
        services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();
        services.AddSingleton<IDistributedLock, RedisDistributedLock>();
        services.AddSingleton<IRequestRateLimiter, FixedWindowRequestRateLimiter>();
        services.AddSingleton<ICircuitBreakerRegistry, CircuitBreakerRegistry>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Rate limiting is applied as middleware (app.UseAtlasRateLimiting()
        // in Program.cs), not as a per-endpoint route, so nothing to map here.
        // TODO: /api/v1/reliability/circuit-breakers (read-only state snapshot via ICircuitBreakerRegistry.SnapshotStates()).
    }
}
