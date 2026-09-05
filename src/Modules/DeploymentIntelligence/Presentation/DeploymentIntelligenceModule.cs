using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.DeploymentIntelligence.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.DeploymentIntelligence.Presentation;

/// <summary>
/// STATUS: real end-to-end now, including the cross-module integration:
/// IDeploymentRegressionService pulls real before/after MetricSample
/// windows from Observability via ISloService.GetSamplesAsync (an
/// Application-layer interface — never ObservabilityDbContext directly,
/// preserving the module-boundary rule) and runs the pure, unit-tested
/// RegressionAnalyzer against them. Exposed via DeploymentsController.
/// </summary>
public class DeploymentIntelligenceModule : IAtlasModule
{
    public string Name => "DeploymentIntelligence";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<DeploymentIntelligenceDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "deploymentintelligence")));

        services.AddScoped<IDeploymentRegressionService, DeploymentRegressionService>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // /api/v1/deployments mapped via Atlas.Web/Controllers/DeploymentsController.
    }
}
