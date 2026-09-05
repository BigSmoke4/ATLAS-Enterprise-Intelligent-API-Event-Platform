using Atlas.Modules.IncidentManagement.Application;
using Atlas.Modules.IncidentManagement.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.IncidentManagement.Presentation;

/// <summary>
/// STATUS: real state machine (Detected -> Investigating -> Mitigating ->
/// Resolved -> PostmortemComplete) with illegal transitions rejected, and
/// MTTD/MTTR computed from timestamps the entity itself records. NOT
/// implemented: automatic incident creation from alerts/SLO breaches (that
/// depends on PolicyEngine + Observability integration), REST endpoints.
/// </summary>
public class IncidentManagementModule : IAtlasModule
{
    public string Name => "IncidentManagement";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<IncidentManagementDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "incidentmanagement")));

        services.AddScoped<IIncidentService, IncidentService>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // /api/v1/incidents mapped via Atlas.Web/Controllers/IncidentsController.
    }
}
