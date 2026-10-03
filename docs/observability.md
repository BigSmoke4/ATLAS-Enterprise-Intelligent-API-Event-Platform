# Observability

ATLAS exposes an OpenTelemetry-backed observability surface for traces and
metrics. It does not invent dashboard values: SLO values are calculated from
persisted `MetricSample` records.

## Implemented

- Serilog structured logging in `Program.cs`.
- ASP.NET Core, HttpClient, runtime, and SQL Client OpenTelemetry
  instrumentation.
- Prometheus scrape endpoint at `/metrics`.
- Docker Compose Prometheus configuration at `observability/prometheus.yml`
  and an optional Grafana container.
- `SloCalculator` real, pure, unit-tested error-budget/burn-rate math.
- `ISloService.GetSamplesAsync` cross-module read seam for deployment
  regression analysis.
- ServiceRegistry background health probing.
- `/health/live` is process-only liveness; `/health/ready` checks PostgreSQL, Redis, and Kafka independently; `/health` exposes the aggregate result.

## Deliberate remaining work

Metric samples are currently recorded through the SLO application service;
request middleware does not yet persist every HTTP outcome because doing so
without service/tenant attribution would produce misleading data. A future
aggregation worker should consume request and dependency meters, apply
retention/rollup rules, and persist only correctly attributed samples.
Prometheus provides live process/runtime metrics now; Grafana dashboards and
trace/log export configuration remain deployment-specific.
