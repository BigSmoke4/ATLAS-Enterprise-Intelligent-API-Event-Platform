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
/// registration, tenant-scoped, EF-backed). REST endpoints
/// (/api/v1/apis, /api/v1/routes) are not yet mapped here — add them once
/// an ApiManagementController exists in Atlas.Web that calls
/// IApiCatalogService, so this stays a thin controller over a real use case
/// rather than a route added ahead of its implementation.
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

        services.AddScoped<IApiCatalogService, ApiCatalogService>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: map /api/v1/apis via a real MVC/minimal-API controller.
    }
}
