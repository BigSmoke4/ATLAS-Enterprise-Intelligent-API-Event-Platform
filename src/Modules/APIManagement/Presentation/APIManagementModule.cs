using Atlas.Modules.APIManagement.Application;
using Atlas.Modules.APIManagement.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.APIManagement.Presentation;

/// <summary>
/// STATUS: Domain + Application + Infrastructure are real (API/version/route
/// registration, tenant-scoped, EF-backed). REST endpoints are mapped by
/// Atlas.Web/Controllers/ApiManagementController. This module also exposes
/// the read-only route-policy contract consumed by Reliability.
/// </summary>
public class APIManagementModule : IAtlasModule
{
    public string Name => "APIManagement";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<ApiManagementDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "apimanagement")));

        // The route policy provider memoizes lookups per request, so it needs
        // access to the current HttpContext (one lookup shared by rate
        // limiting, telemetry attribution and timeout/retry resolution).
        services.AddHttpContextAccessor();

        services.AddScoped<IApiCatalogService, ApiCatalogService>();
        services.AddScoped<IRoutePolicyProvider, ApiRoutePolicyProvider>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Nothing to map here by design. The module's HTTP surface is
        // ApiManagementController in Atlas.Web (a thin [ApiController] over
        // IApiCatalogService); modules expose behaviour through their
        // application services and registration, not through routes.
    }
}
