# Performance

No latency figures are published in this repository, because any number
produced here would come from a shared CI runner rather than the environment
the platform runs in. What the repository does contain is the machinery to
*measure* performance, and one pipeline gate that fails when the platform stops
answering fast enough.

## What runs today

1. **k6 smoke gate in CI** (`tests/Atlas.PerformanceTests/atlas-smoke.js`, run
   by the *Performance smoke test* step in `.github/workflows/ci.yml` after the
   integration suites). It starts the real host against the CI PostgreSQL,
   Redis and Kafka services and asserts the contracts an operator would
   notice first:
   - `GET /health/live` → `200`
   - `GET /health/ready` → `200` (dependency-aware)
   - `GET /` → `302` to sign-in (routing, MVC and the cookie challenge work)
   - `GET /api/v1/organizations` anonymously → `401` (never an HTML redirect)

   Thresholds: `http_req_failed < 1%`, every check passing, and
   `p(95) < 250 ms` for the liveness endpoint — the same target the platform
   aims at for API calls, applied only to the endpoint whose budget it can
   honour (readiness deliberately touches three dependencies and is not given
   a latency budget).

   Two details make the gate honest rather than flaky: the step waits for
   `/health/ready` before measuring, so a dependency warm-up is not reported as
   a platform failure, and the console probe disables redirect following
   (`redirects: 0`) because the contract is the `302` challenge itself — k6
   follows redirects by default and would otherwise observe the sign-in page's
   `200`. The deliberate `401` is registered through `http.expectedStatuses` so
   `http_req_failed` only counts genuinely unexpected responses. The measured
   summary is uploaded with the CI diagnostics as `k6-smoke-summary.json`, and
   on failure `scripts/k6-summary-annotations.py` republishes the failing
   checks and threshold values as run annotations, because a workflow log is
   not readable without repository access. Nothing from that run is copied into
   the docs.
2. **Per-route percentiles from recorded telemetry** — the Observability module
   aggregates every request into one-minute `RequestTelemetryAggregate` rows
   and computes P50/P95/P99, error rates and throughput from them
   (`ITelemetryQueryService`). `/api/v1/metrics/{summary,services,routes,series}`
   serves those numbers and every response states whether it has data; the
   Grafana dashboard `observability/grafana/dashboards/atlas-overview.json`
   renders the same series. No percentile is ever invented — with no recorded
   requests the API answers `hasData: false` and the UI prints
   "No telemetry available.".
3. **Container smoke test in CI** — asserts liveness, readiness and the
   Prometheus `http_server_request_duration*` metric family from the running
   container, so instrumentation regressions fail the pipeline.
4. **Redis rate-limit correctness under concurrency** — an integration test
   drives concurrent increments against a real Redis and asserts the counters
   are atomic, i.e. the limiter stays correct with more than one node.

## Running a real load test

```bash
# Against any environment; the script reports what it actually observed.
ATLAS_BASE_URL=https://atlas.example.com VUS=25 DURATION=5m \
  k6 run tests/Atlas.PerformanceTests/atlas-smoke.js

# Then read the platform's own measurements for the same window:
curl -s "https://atlas.example.com/api/v1/metrics/routes?organizationId=<org>&windowMinutes=5" | jq
```

When a real environment is measured, record the environment (runner size,
database size, replica count), the traffic profile and the raw P50/P95/P99 —
and put those numbers in the SLO review, not here. The targets the platform is
designed against are a median under 100 ms and a P95 under 250 ms for API
calls, and under one second for a fully rendered command-centre dashboard;
they are acceptance criteria for a deployment, not claims of this repository.
