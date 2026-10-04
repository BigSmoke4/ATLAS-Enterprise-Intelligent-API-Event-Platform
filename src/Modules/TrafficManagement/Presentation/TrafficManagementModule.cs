using Atlas.Modules.TrafficManagement.Application;
using Atlas.Modules.TrafficManagement.Infrastructure;
using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.TrafficManagement.Presentation;

/// <summary>
/// STATUS: real end-to-end for all routing strategies — RoutingStrategies
/// (Domain/) are pure, unit-tested algorithms; TrafficRoutingService wires
/// them to REAL per-instance health via ServiceRegistry.IServiceHealthService
/// (an Application-layer interface, never the ServiceRegistry DbContext) and,
/// for LeastConnections/LatencyBased, to REPORTED per-instance gauges held by
/// IInstanceTelemetryService (POST /api/v1/traffic/telemetry). Reports older
/// than TrafficManagement:TelemetryStalenessSeconds (default 60s) are
/// excluded rather than trusted; with no fresh reports the route fails with
/// an explicit reason instead of fabricating values.
/// </summary>
public class TrafficManagementModule : IAtlasModule
{
    public string Name => "TrafficManagement";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");
        services.AddDbContext<TrafficManagementDbContext>(options =>
            options.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "trafficmanagement")));
        services.AddScoped<ITrafficPolicyService, TrafficPolicyService>();

        // Singletons: the telemetry store must be shared across requests, and
        // TimeProvider.System is the clock both store and router use for the
        // staleness window (overridable via config, injectable in tests).
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IInstanceTelemetryService, InMemoryInstanceTelemetryService>();
        var stalenessSeconds = configuration.GetValue("TrafficManagement:TelemetryStalenessSeconds", 60);
        services.AddScoped<ITrafficRoutingService>(sp => new TrafficRoutingService(
            sp.GetRequiredService<IServiceHealthService>(),
            sp.GetRequiredService<ITrafficPolicyService>(),
            sp.GetRequiredService<IInstanceTelemetryService>(),
            sp.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(stalenessSeconds)));
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // /api/v1/traffic mapped via Atlas.Web/Controllers/TrafficController.
    }
}
