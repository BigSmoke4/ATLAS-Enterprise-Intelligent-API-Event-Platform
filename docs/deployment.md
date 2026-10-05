# Deployment

`docker-compose.yml` brings up Postgres, Redis, Kafka (+ Zookeeper), Kafka UI,
Prometheus, Grafana, and `atlas-web` built from the included multi-stage
`Dockerfile`. Configuration is via environment variables /
`appsettings.*.json`; no credentials are committed and `.env.example`
documents required variables.

## Release order

1. Provision PostgreSQL, Redis, and Kafka.
2. Run the reviewed EF migrations with `scripts/migrate.sh` using a temporary
   schema-migration identity.
3. Deploy the application with a restricted runtime database identity.
4. Verify `/health/live`, then `/health/ready`.
5. Enable development seeding only in development environments.
6. Monitor `/metrics`, logs, traces, and deployment regression analysis.

## CI integration environment

GitHub Actions provisions PostgreSQL, Redis, Zookeeper, and Kafka. The
WebApplicationFactory integration suite verifies API authorization and
readiness against those services before the Docker build. The dependency
vulnerability command is currently advisory (`continue-on-error`) because
NuGet advisory output requires independent review; a reported advisory must
not be interpreted as a clean security result. Readiness failures
are surfaced as dependency failures rather than masked by a hard-coded 200.

For a running Compose deployment, execute `scripts/smoke.sh` (or set
`ATLAS_BASE_URL`) to verify liveness, dependency readiness, and the
Prometheus-compatible metrics endpoint.

## Production readiness checklist

Everything below is either already implemented in this repository or is a
one-line configuration change; the right-hand column says which.

**Configuration and secrets**

- [ ] `ConnectionStrings:Postgres`, `ConnectionStrings:Redis`,
      `Kafka:BootstrapServers` supplied as environment variables or secrets —
      never in `appsettings*.json` (`.env.example` documents every variable).
- [ ] `AI:AnthropicApiKey` set only if the AI completion provider is wanted;
      without it the assistant still answers from tool evidence (deterministic).
- [ ] HTTPS terminated in front of the app; HSTS and security headers are
      applied by `UseAtlasSecurityHeaders()` and the cookie policy.
- [ ] Development seeding **off** (`Seed:Development` unset): the seeded admin
      account exists only for local use.
- [ ] GitHub secret scanning **with push protection** enabled on the repository
      (Settings → Code security): CI already runs two layers (`secret-scan.sh`
      and the gitleaks history job), but push protection is the only one that
      refuses a credential before it enters the remote history at all.

**Data**

- [ ] `scripts/migrate.sh` run as a release step with a schema-migration
      identity; the runtime identity has DML rights only.
- [ ] Backups + PITR configured on PostgreSQL, with a restore drill completed
      against the targets in `docs/disaster-recovery.md`.
- [ ] Telemetry retention (14 days) matches the operator's expectation; audit
      retention/archival decided explicitly (the app never deletes audit rows).

**Observability**

- [ ] Prometheus scraping `/metrics`; Grafana provisioned from
      `observability/`; log aggregation wired to the Serilog JSON output.
- [ ] `OpenTelemetry:Otlp:Endpoint` set if traces should leave the process.
- [ ] Alert on `/health/ready` (orchestrator probe) and on
      `GET /api/v1/alerts`; dashboards for host-level Kafka/PostgreSQL/Redis
      internals come from their exporters, not from a fabricated panel.
- [ ] Kafka consumer lag observed (`/api/v1/metrics/infrastructure` or the
      Observability console) with an alert threshold chosen per environment.

**Performance and resilience**

- [ ] k6 smoke (`tests/Atlas.PerformanceTests/atlas-smoke.js`) executed against
      the release candidate; recorded numbers reviewed against the targets
      (median < 100 ms, P95 < 250 ms, dashboard < 1 s).
- [ ] Rate-limit scopes/algorithms configured per route, and the fail-open
      Redis policy accepted (documented in ADR-004) — including the alert on
      `atlas.ratelimit.store.failures`.
- [ ] Kafka optionality confirmed: recording a deployment succeeds while the
      broker is down (the event is logged and counted, not silently dropped).

**Release hygiene**

- [ ] CI green on the release commit — build, `-warnaserror` static analysis,
      unit/architecture/integration suites, EF migration application + model
      drift check, JavaScript syntax gate, Docker build + container smoke.
- [ ] Dependency-advisory output reviewed manually (the step is advisory by
      design; a green run is not a security verdict).
- [ ] Rollback rehearsed: previous image tag deploys cleanly because migrations
      are additive and forward-only.
