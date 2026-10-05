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
using System.Globalization;

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
                // Sampling policy is configuration, not code: a full deployment
                // sets OpenTelemetry:Traces:SamplerRatio to a value between 0 and
                // 1 (e.g. 0.1 for 10% of traces) and gets a parent-based
                // trace-id-ratio sampler, which keeps a trace coherent across
                // services because the decision is derived from the trace id.
                // Absent = always on, which is the right default for a control
                // plane whose request volume is human-scale.
                var samplerRatio = configuration["OpenTelemetry:Traces:SamplerRatio"];
                if (!string.IsNullOrWhiteSpace(samplerRatio))
                {
                    if (!double.TryParse(samplerRatio, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio) ||
                        ratio is < 0 or > 1)
                    {
                        throw new InvalidOperationException(
                            $"OpenTelemetry:Traces:SamplerRatio must be a number between 0 and 1 inclusive; '{samplerRatio}' is not. " +
                            "Use 1 to keep every trace, or remove the setting for the default (always on).");
                    }

                    tracing.SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio)));
                }
                else
                {
                    tracing.SetSampler(new ParentBasedSampler(new AlwaysOnSampler()));
                }

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
