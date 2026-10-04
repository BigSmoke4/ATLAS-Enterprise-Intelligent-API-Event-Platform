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
/// auto-instrumentation is wired for tracing; OTLP span export activates
/// when OpenTelemetry:Otlp:Endpoint is configured. Metrics export to
/// Prometheus via /metrics. NOT implemented: log/trace correlation-ID
/// propagation
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
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation()
                    // Npgsql ships its own ActivitySource — this is how PostgreSQL
                    // statements appear in distributed traces without any
                    // provider-specific instrumentation package.
                    .AddSource("Npgsql")
                    .AddSource("Atlas");

                // Optional OTLP export: set OpenTelemetry:Otlp:Endpoint (e.g.
                // http://otel-collector:4317) to ship spans to any
                // OpenTelemetry Protocol collector. Unset = tracing stays
                // in-process only, with zero behavior change.
                var otlpEndpoint = configuration["OpenTelemetry:Otlp:Endpoint"];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Atlas")
                .AddPrometheusExporter());
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // TODO: /api/v1/slo (compliance/error-budget read endpoints).
    }
}
