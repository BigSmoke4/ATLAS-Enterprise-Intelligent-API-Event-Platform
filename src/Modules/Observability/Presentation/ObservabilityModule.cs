using Atlas.Modules.Observability.Application;
using Atlas.Modules.Observability.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Atlas.Modules.Observability.Presentation;

/// <summary>
/// STATUS: real end-to-end.
///
/// * SLO / error-budget math is pure and unit-tested (Domain/SloCalculator),
///   and compliance is now derived from two real sources: live request
///   telemetry (RequestTelemetryMiddleware -> TelemetryIngestBuffer ->
///   TelemetryAggregationService -> one-minute RequestTelemetryAggregate
///   buckets) and explicitly recorded MetricSamples (probed availability plus
///   pushed samples).
/// * OpenTelemetry ASP.NET Core/HttpClient/runtime instrumentation feeds
///   Prometheus via /metrics; ATLAS-defined instruments live in
///   Atlas.Shared.Observability.AtlasMetrics on the "Atlas" meter.
/// * OTLP span export activates when OpenTelemetry:Otlp:Endpoint is set.
///
/// Deliberately NOT implemented (documented in docs/observability.md): log
/// export to an external collector (Serilog console/file sinks are wired in
/// Atlas.Web) and long-term metric storage beyond PostgreSQL retention.
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
        services.AddScoped<ITelemetryQueryService, TelemetryQueryService>();

        // Telemetry ingest: one bounded buffer instance shared by the request
        // path (writer) and the aggregation background service (reader).
        services.AddSingleton<TelemetryIngestBuffer>();
        services.AddSingleton<ITelemetryIngestService>(sp => sp.GetRequiredService<TelemetryIngestBuffer>());
        services.Configure<TelemetryAggregationOptions>(configuration.GetSection("Telemetry"));

        // The Testing environment runs the aggregation service only when a
        // PostgreSQL connection is explicitly provided; the integration test
        // host otherwise has no schema to write telemetry into.
        var isTesting = string.Equals(configuration["ASPNETCORE_ENVIRONMENT"], "Testing", StringComparison.OrdinalIgnoreCase);
        var hasDatabase = !string.IsNullOrWhiteSpace(configuration.GetConnectionString("Postgres"));
        if (!isTesting || hasDatabase)
        {
            services.AddHostedService<TelemetryAggregationService>();
        }

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
                // http://otel-collector:4317) to ship spans to any OpenTelemetry
                // Protocol collector. Unset = in-process only, zero behavior change.
                var otlpEndpoint = configuration["OpenTelemetry:Otlp:Endpoint"];
                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
            })
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(Atlas.Shared.Observability.AtlasMetrics.MeterName)
                .AddPrometheusExporter());
    }

    public void RegisterEndpoints(IEndpointRouteBuilder endpoints)
    {
        // Telemetry and SLO read endpoints are mapped by Atlas.Web controllers:
        // /api/v1/metrics/* (MetricsController) and /api/v1/slo/* (SloController).
        // The telemetry middleware itself is installed by Program.cs.
    }
}
