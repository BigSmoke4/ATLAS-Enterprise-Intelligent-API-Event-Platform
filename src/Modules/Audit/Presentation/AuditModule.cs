using Atlas.Modules.Audit.Application;
using Atlas.Modules.Audit.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.Audit.Presentation;

/// <summary>
/// STATUS: real — append-only enforced in AuditDbContext, IAuditLogger is
/// injectable from any other module's Application layer. Not yet wired: no
/// module currently calls IAuditLogger.RecordAsync (that happens as each
/// module's write use cases are built out), and there's no
/// /api/v1/audit read endpoint yet.
/// </summary>
public class AuditModule : IAtlasModule
{
    public string Name => "Audit";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<AuditDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "audit")));

        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<IAuditQueryService, AuditQueryService>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: /api/v1/audit (read-only, filterable) once needed by a real caller.
    }
}
