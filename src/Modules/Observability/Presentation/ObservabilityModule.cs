using Atlas.Modules.Observability.Application;
using Atlas.Modules.Observability.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

namespace Atlas.Modules.Observability.Presentation;

/// <summary>
/// STATUS: SLO/error-budget math is real, pure, and unit-testable
/// (Domain/SloCalculator) — compliance is always derived from recorded
/// MetricSample rows, never a fabricated number. OpenTelemetry ASP.NET Core
/// auto-instrumentation is wired for tracing. NOT implemented: a metrics
/// exporter wired to Prometheus, log/trace correlation-ID propagation
/// helpers, and the background aggregation job that would populate
/// MetricSamples automatically from live request traffic (right now
/// RecordOutcomeAsync/RecordLatencyAsync must be called explicitly).
/// </summary>
public class ObservabilityModule : IAtlasModule
{
    public string Name => "Observability";

    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is not configured.");

        services.AddDbContext<ObservabilityDbContext>(opt =>
            opt.UseNpgsql(connectionString, npg => npg.MigrationsHistoryTable("__EFMigrationsHistory", "observability")));

        services.AddScoped<ISloService, SloService>();

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddSource("Atlas"))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter("Atlas"));
        // TODO: .AddOtlpExporter() / Prometheus exporter once an endpoint is decided.
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: /api/v1/slo (compliance/error-budget read endpoints).
    }
}
