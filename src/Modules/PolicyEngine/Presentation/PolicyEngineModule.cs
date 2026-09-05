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
/// Domain/PolicyCondition. IPolicyEvaluator is intentionally read-only:
/// nothing in this module executes an action automatically yet. Wiring a
/// matched rule to actually flip Reliability's CircuitBreaker or notify
/// IncidentManagement is planned and must go through explicit
/// authorization per the AI-safety section of the master prompt.
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
        // TODO: /api/v1/policies CRUD once needed by a real caller.
    }
}
