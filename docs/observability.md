# Observability

ATLAS exposes an OpenTelemetry-backed observability surface for traces and
metrics. It does not invent dashboard values: every latency percentile,
error ratio, SLO value and infrastructure reading is derived from recorded
telemetry, and each surface states its evidence — live request aggregates
first, then persisted `MetricSample` rows, and `hasData: false` (rendered as
"No telemetry available.") when there is nothing recorded.

## Implemented

- Serilog structured logging in `Program.cs`.
- ASP.NET Core, HttpClient, and .NET runtime OpenTelemetry instrumentation;
  Prometheus exporter at `/metrics`. PostgreSQL spans come from Npgsql's
  built-in `Npgsql` ActivitySource (the SQL Server-only
  `OpenTelemetry.Instrumentation.SqlClient` package was deliberately
  removed — it never traced this stack).
- **Optional OTLP span export**: set `OpenTelemetry:Otlp:Endpoint` (e.g.
  `http://otel-collector:4317`) and spans stream to any OpenTelemetry
  Protocol collector. When unset, no exporter is registered and behavior is
  unchanged.
- Docker Compose Prometheus configuration at `observability/prometheus.yml`,
  and Grafana with **file-provisioned** Prometheus datasource and the
  "ATLAS Platform Overview" dashboard
  (`observability/grafana/provisioning/`, `observability/grafana/dashboards/`),
  mounted by `docker-compose.yml`.
- `SloCalculator` real, pure, unit-tested error-budget/burn-rate math.
- `ISloService.GetSamplesAsync` cross-module read seam for deployment
  regression analysis; `ISloService.ListSlosAsync` feeds the command-center
  dashboard's per-SLO compliance/error-budget panel.
- **Prober-driven availability telemetry**: every `HealthCheckProberService`
  probe outcome is persisted as an `Availability` `MetricSample` via
  `ISloService.RecordOutcomeAsync` (a failed telemetry write is logged and
  isolated, never rolled back into the health record), so availability SLOs
  burn down from measured checks, not manual API calls. SREs can also push
  external request telemetry via `POST /api/v1/slo/samples/outcome|latency`.
- `/health/live` is process-only liveness; `/health/ready` checks PostgreSQL, Redis, and Kafka independently; `/health` exposes the aggregate result.
- The CI Docker smoke test asserts the Prometheus metric family
  (`http_server_request_duration*`) is actually exported.
- **Request telemetry pipeline (recorded, not sampled stories):** the
  middleware observes every non-static request, normalises the route
  (`{id}`/`{n}`), attributes the service through the route-policy provider and
  writes into a bounded in-process channel (50 000, `DropWrite` — back-pressure
  is never applied to the request path; drops are counted and exported).
  `TelemetryAggregationService` merges those observations into one-minute
  `RequestTelemetryAggregates` buckets every 5 s, retains them for 14 days and
  re-queues a batch if PostgreSQL is unavailable. Percentiles are **histogram
  bucket boundaries** (5 ms … 10 000 ms plus an overflow bucket), never
  interpolated estimates.
- **Read surface:** `GET /api/v1/metrics/{summary,services,routes,series,infrastructure}`.
  Each response carries `hasData`/`available` plus the reason when a source
  could not be read. Infrastructure readings come from PostgreSQL's own
  `pg_stat_activity`/`pg_stat_database` views (`DatabaseMetricsProbe`), the Redis
  cache statistics counters, the ingest-buffer statistics, and the Kafka
  consumer-lag registry (which reports `available: false` with a staleness
  reason instead of a stale number).
- **Alerting is derived, never stored:** `GET /api/v1/alerts` recomputes the
  active alert list from registry health, SLO compliance, live breaker states,
  consumer lag and the dead-letter backlog, and each item names the evidence it
  came from. `alerts: []` with `hasEvidence: true` means "checked and clear".
- **Instrument catalogue** (meter `Atlas`; the Prometheus exporter prefixes the
  exported name with the sanitised meter name, so `/metrics` is the source of
  truth for the exact spelling in a given deployment):
  `atlas.http.requests`, `atlas.http.request.errors`,
  `atlas.http.request.client_errors`, `atlas.http.request.duration`,
  `atlas.telemetry.samples.dropped`, `atlas.telemetry.flush.failures`,
  `atlas.telemetry.buckets.persisted`, `atlas.events.published`,
  `atlas.events.consumed`, `atlas.events.dead_lettered`, `atlas.events.retries`,
  `atlas.ratelimit.rejections`, `atlas.ratelimit.store.failures`,
  `atlas.circuitbreaker.state.changes`, `atlas.cache.hits`, `atlas.cache.misses`,
  `atlas.ai.tool.invocations`, `atlas.ai.answers.insufficient_evidence`.

## Deliberate remaining work

- **Long-term metrics storage.** Aggregates live in PostgreSQL and expire after
  14 days; shipping the same instruments to Prometheus/Mimir for year-scale
  retention is deployment work, not platform code.
- **Grafana panels for container internals.** Kafka, PostgreSQL and Redis
  *internals* need their own exporters (or a Prometheus scrape of a managed
  service) before panels can be drawn; the provisioned dashboard therefore
  charts the platform's HTTP and runtime metrics only, and this document lists
  the ATLAS instrument names so additional panels can be added against the
  exporter's real naming instead of an invented one.
- **Trace sampling policy.** Spans are exported when
  `OpenTelemetry:Otlp:Endpoint` is set; choosing tail-based sampling and a
  collector topology is deployment-specific.
- **Telemetry backfill.** When a flush fails, buckets are retained in memory and
  retried; a restart loses what was buffered (a bounded, documented trade-off
  against writing telemetry inside the request path).
