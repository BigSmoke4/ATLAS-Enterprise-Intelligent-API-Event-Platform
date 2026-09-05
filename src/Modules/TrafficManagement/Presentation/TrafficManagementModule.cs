using Atlas.Modules.TrafficManagement.Application;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.TrafficManagement.Presentation;

/// <summary>
/// STATUS: real end-to-end for RoundRobin and Weighted — RoutingStrategies
/// (Domain/) are real, pure, unit-tested algorithms, and
/// TrafficRoutingService now wires them to REAL per-instance health data
/// via ServiceRegistry.IServiceHealthService.GetInstancesAsync (an
/// Application-layer interface — never ServiceRegistry's DbContext
/// directly). LeastConnections and LatencyBased are honestly refused with
/// a clear reason (ATLAS doesn't measure per-instance active-connection
/// count or latency anywhere yet) rather than fed fabricated zero values
/// that would make every instance look artificially tied.
/// </summary>
public class TrafficManagementModule : IAtlasModule
{
    public string Name => "TrafficManagement";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ITrafficRoutingService, TrafficRoutingService>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // /api/v1/traffic mapped via Atlas.Web/Controllers/TrafficController.
    }
}
