using Atlas.Modules.PolicyEngine.Application;
using Atlas.Modules.PolicyEngine.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.PolicyEngine.Presentation;

/// <summary>
/// STATUS: real, safe rule representation and evaluator (data-driven
/// field/operator/value conditions — no arbitrary code execution) — see
/// Domain/PolicyCondition. Policy versions are immutable in history and
/// matched RaiseAlert rules emit through the shared incident-alert contract;
/// circuit-breaker and rollback actions remain advisory until their explicit
/// authorization/confirmation workflows are implemented.
/// </summary>
public class PolicyEngineModule : IAtlasModule
{
    public string Name => "PolicyEngine";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<PolicyEngineDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "policyengine")));

        services.AddScoped<IPolicyEvaluator, PolicyEvaluator>();
        services.AddScoped<IPolicyManagementService, PolicyManagementService>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // /api/v1/policies mapped via Atlas.Web/Controllers/PolicyController.
    }
}
