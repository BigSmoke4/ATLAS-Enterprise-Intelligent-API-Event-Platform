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
/// currently use one process-wide default (100 req/min per IP or API key
/// prefix) rather than per-route config from APIManagement.ApiRoute — that
/// integration is the next real gap, not a fabricated one.
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
