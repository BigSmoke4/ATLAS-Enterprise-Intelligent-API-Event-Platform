# Observability

ATLAS exposes an OpenTelemetry-backed observability surface for traces and
metrics. It does not invent dashboard values: SLO values are calculated from
persisted `MetricSample` records.

## Implemented

- Serilog structured logging in `Program.cs`.
- ASP.NET Core, HttpClient, and .NET runtime OpenTelemetry instrumentation;
  Prometheus exporter at `/metrics`. PostgreSQL spans come from Npgsql's
  built-in `Npgsql` ActivitySource (the SQL Server-only
  `OpenTelemetry.Instrumentation.SqlClient` package was deliberately
  removed — it never traced this stack).
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

## Deliberate remaining work

Request middleware does not yet persist every HTTP outcome as a sample,
because doing so without unambiguous service/tenant attribution would
produce misleading SLO data. A future aggregation worker should consume
request/dependency meters, apply retention/rollup rules, and persist only
correctly attributed samples. Container/queue-level metrics (Kafka consumer
lag, Postgres internals) require exporters on those services in Compose;
trace export to a collector (OTLP/Jaeger) remains deployment-specific.
