using Atlas.Shared.Contracts;
using Atlas.Modules.Organizations.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.Organizations.Presentation;

public class OrganizationsModule : IAtlasModule
{
    public string Name => "Organizations";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();
        services.AddDbContext<OrganizationsDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "organizations")));
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Mapped via Atlas.Web/Controllers/OrganizationsController (MVC), thin controller -> application service.
    }
}
