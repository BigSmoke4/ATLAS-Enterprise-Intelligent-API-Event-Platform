# Observability

Target: OpenTelemetry traces/metrics from Gateway → API → Service →
Database → Kafka → Consumer, Prometheus-scrapeable metrics endpoint,
structured Serilog logs correlated by trace/correlation ID.

## Implemented

- Serilog wired in `Program.cs` (structured logging).
- OpenTelemetry ASP.NET Core tracing/metrics instrumentation registered
  (`ObservabilityModule`) — no exporter configured yet (see below).
- `SloCalculator` — real, pure, unit-tested error-budget/burn-rate math,
  always derived from recorded `MetricSample` rows.
- `ISloService.GetSamplesAsync` — the real cross-module read seam
  `DeploymentIntelligence` uses for regression analysis (Application
  interface, never `ObservabilityDbContext` directly).
- ServiceRegistry's `HealthCheckProberService` — a real automated
  `BackgroundService` that HTTP-probes every registered instance on a
  timer and feeds results into `ServiceInstance.RecordHealthCheck`,
  closing the "health only updates when someone calls the API" gap.

## Not yet implemented

- No metrics exporter (Prometheus/OTLP) is configured — traces/metrics are
  instrumented but have nowhere to go yet; add `.AddPrometheusExporter()`
  or `.AddOtlpExporter()` in `ObservabilityModule` once a collector target
  is chosen.
- No background job automatically populates `MetricSample` rows from real
  request traffic — `RecordOutcomeAsync`/`RecordLatencyAsync` must be
  called explicitly today (e.g. from middleware, which isn't wired in yet).
- The dashboard (`/Dashboard`) intentionally shows "No telemetry
  available." until the above lands — this is correct behavior per the
  master prompt's "no fabricated metrics" rule, not a bug.
