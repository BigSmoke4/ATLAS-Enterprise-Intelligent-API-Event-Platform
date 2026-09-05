using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Modules.ServiceRegistry.Presentation;

/// <summary>
/// STATUS: real end-to-end, including automated health checks —
/// HealthCheckProberService (BackgroundService) now polls every registered
/// instance's HTTP health endpoint on a timer and feeds real results into
/// ServiceInstance.RecordHealthCheck, closing the previous gap where health
/// only updated via a manual API call. Configurable via
/// ServiceRegistry:HealthCheck:IntervalSeconds /
/// ServiceRegistry:HealthCheck:TimeoutSeconds /
/// ServiceRegistry:HealthCheck:Path in appsettings.json.
/// </summary>
public class ServiceRegistryModule : IAtlasModule
{
    public string Name => "ServiceRegistry";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<ServiceRegistryDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "serviceregistry")));

        services.AddScoped<IServiceHealthService, ServiceHealthService>();

        services.Configure<HealthCheckProberOptions>(opt =>
        {
            var intervalSeconds = configuration.GetValue<int?>("ServiceRegistry:HealthCheck:IntervalSeconds");
            var timeoutSeconds = configuration.GetValue<int?>("ServiceRegistry:HealthCheck:TimeoutSeconds");
            var path = configuration["ServiceRegistry:HealthCheck:Path"];
            if (intervalSeconds.HasValue) opt.Interval = TimeSpan.FromSeconds(intervalSeconds.Value);
            if (timeoutSeconds.HasValue) opt.RequestTimeout = TimeSpan.FromSeconds(timeoutSeconds.Value);
            if (!string.IsNullOrWhiteSpace(path)) opt.HealthPath = path;
        });
        services.AddHttpClient(nameof(HealthCheckProberService));
        services.AddHostedService<HealthCheckProberService>();
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // /api/v1/services mapped via Atlas.Web/Controllers/ServicesController.
    }
}
