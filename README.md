# ATLAS — Enterprise Intelligent API & Event Platform

[![CI](https://github.com/BigSmoke4/ATLAS-Enterprise-Intelligent-API-Event-Platform/actions/workflows/ci.yml/badge.svg)](https://github.com/BigSmoke4/ATLAS-Enterprise-Intelligent-API-Event-Platform/actions/workflows/ci.yml)

ATLAS is an evidence-first **ASP.NET Core 9 MVC/Razor modular monolith** for API management, service health, event processing, reliability engineering, observability, incident response, deployment intelligence, policy automation, and AI-assisted operations. One deployable process, one PostgreSQL database, strict module boundaries enforced by executable architecture tests — designed so any module can later be extracted into its own service.

The platform's defining rule is the **No Fake Functionality** contract: metrics, SLO compliance, health states, AI answers, and root-cause findings are rendered only when derived from recorded data. Where a capability is planned rather than implemented, the UI, APIs, and this README say so explicitly instead of showing a synthetic value.

## Status & verification

The full delivery pipeline is green and verifies the application end-to-end on every push:

- **Restore + Build + Static analysis** (`dotnet build -warnaserror`) — nullable-enabled C# across 18 projects.
- **150 unit test methods** (`[Fact]`/`[Theory]` — theory data rows expand the executed case count in the run log) — routing strategies incl. telemetry-driven selection and staleness, sliding/token-bucket/leaky-bucket + fixed-window rate limiting, circuit-breaker state machine, policy evaluator + versioning, SLO/error-budget math, canary/regression analyzers, root-cause scoring, incident state machine + MTTD/MTTR, organizations/tenant rules, API-key hashing, webhook HMAC verification, event contract validation, event dispatcher/coordinator, dead-letter service, audit query service, organization-access handler, the list-endpoint sort whitelists (`SortSpecTests`), the OIDC configuration contract (`OidcOptionsTests`), the optimistic-concurrency token sweep across all eleven module models (`ConcurrencyTokenConfigurationTests`), the 409 mapping (`ConcurrencyConflictExceptionHandlerTests`), the DTO wire-shape guard (`ResponseDtoContractTests`), the hostile-input envelope boundary (`EventContractValidatorTests`), and the session-revalidation decision (`IdentitySessionValidationTests`).
- **EF Core migrations are checked in** — one `InitialSchema` migration plus a model snapshot per module (`src/Modules/*/Infrastructure/Migrations`, 11 contexts, 11 PostgreSQL schemas) — and CI applies them to a real PostgreSQL 16 on every run. `EnsureCreated` is never used. CI additionally runs `dotnet ef migrations add` and fails if the model has drifted from the checked-in snapshot; the **Generate EF migrations** workflow regenerates and commits them on a runner that has the SDK and `dotnet-ef`.
- **Architecture tests** (NetArchTest) — Domain/Application layers of one module may not depend on another module's Infrastructure; MVC controllers cannot reference EF Core.
- **Integration tests** (WebApplicationFactory + real PostgreSQL/Redis/Kafka, 51 test methods): anonymous rejection with 401, unauthenticated liveness, organization isolation (403 for cross-tenant reads), role enforcement (403 for non-admin policy operations), tenant-scoped audit reads, dependency-aware readiness, atomic Redis rate-limit counters under concurrency, Kafka publisher headers/payload round-trip, every console page rendered through the real MVC pipeline as an operator (a render-time Razor defect can no longer hide behind a 200), the antiforgery filter's skip rules for API-key and non-cookie principals, list-endpoint sorting (whitelisted fields reorder real results; unknown fields and directions answer `400 INVALID_SORT_FIELD`/`INVALID_SORT_DIRECTION`), and the read-endpoint DTO contracts — incidents (including a real detect-then-transition update against PostgreSQL with the `xmin` token in place), deployments, dead letters, policies and audit, each asserted on the wire for DTO fields and for the absence of `rowVersion`/`domainEvents`; idempotent consumption against real PostgreSQL (a redelivered event is claimed once and stays claimed across contexts, a released claim is re-taken after a handler failure, two consumers racing the same event yield exactly one claim, and claims are scoped per consumer group — `EventIdempotencyIntegrationTests`); and the session-revocation authorization surface (unknown user `404` rather than a silent success, self-deactivation refused with `400`, non-admin revocation `403`).
- **Docker build + container smoke test** — liveness, readiness, and the Prometheus `http_server_request_duration*` metric family asserted from the running container.

## Technology stack

| Area | Choice |
| --- | --- |
| Runtime | .NET 9, nullable reference types, `LangVersion=latest` |
| Web | ASP.NET Core MVC + Razor Views, vanilla ES-module JS, centralized CSS — no SPA framework |
| Data | PostgreSQL 16 + EF Core 9 (Npgsql), per-module schemas & DbContexts, migrations only |
| Cache/Coordination | Redis 7 (StackExchange.Redis) behind `ICacheService`/`IDistributedLock`/`IRateLimitStore` |
| Events | Apache Kafka (Confluent.Kafka) behind `IEventPublisher`/`IEventConsumer` + retry/DLQ/idempotency |
| Observability | OpenTelemetry (ASP.NET Core/HttpClient/runtime + Npgsql ActivitySource), Prometheus exporter at `/metrics`, Serilog structured logs |
| Testing | xUnit, WebApplicationFactory, NetArchTest.Rules |
| CI/CD | GitHub Actions + Docker multi-stage build |

## Architecture

```
Razor View → MVC Controller (thin) → Application Service → Domain → Infrastructure → PostgreSQL / Redis / Kafka
```

Modules (`src/Modules/*`): Identity, Organizations, APIManagement, TrafficManagement, ServiceRegistry, EventPlatform, Reliability, Observability, IncidentManagement, DeploymentIntelligence, PolicyEngine, AIOperations, Audit. Each has `Domain / Application / Infrastructure / Presentation` folders. Cross-module calls go only through Application interfaces or `src/Shared` contracts — never another module's DbContext or infrastructure. Design rationale lives in `docs/decisions/ADR-001…010`.

## What is implemented (verified)

**Platform & tenancy**
- ASP.NET Core Identity (email/password), lockout, secure password policy, cookie + API-key auth, role RBAC (`PlatformAdmin`, `OrganizationAdmin`, `SRE`, `Developer`, `SecurityEngineer`, `Operator`, `Viewer`), one-time-show API keys stored hashed (prefix + SHA-256), roles seeded idempotently at startup.
- Tenant isolation at three layers: EF Core global query filters keyed off `ITenantContext`, the `SameOrganization` authorization requirement (query/route org must match the `org_id` claim; PlatformAdmin bypass documented), and per-controller checks — enforced uniformly across JSON APIs **and** the Razor pages (`/Apis`, `/Services`, `/Incidents`, `/Observability` resolve the caller's org from their claim and 403 on explicit foreign organizations; AI tool access is tenant-checked), covered by integration tests (cross-tenant reads return 403).
- Append-only audit trail (DbContext rejects UPDATE/DELETE), before/after JSON, read API gated to `AuditRead` (SecurityEngineer/PlatformAdmin); unscoped reads return system records only. The deployment role's grants can enforce the same rule in PostgreSQL — the statements are in `docs/security.md`.

**API & traffic management, reliability**
- API/version/route catalog with tenant scoping and route policy configuration (rate limit scope/algorithm/window/timeout), consumed by enforcement through the `IRoutePolicyProvider` contract.
- Traffic policies (round-robin, weighted, priority, least-connections, latency-based, canary, blue/green) routing across **real healthy registered instances**; least-connections and latency-based strategies run on per-instance gauges reported via `POST /api/v1/traffic/telemetry` (active connections / average latency), trusted only inside a staleness window — instances with stale or missing reports are excluded, never fed fabricated inputs.
- Distributed rate limiting on the live pipeline (fixed window default; token bucket, sliding window, and leaky bucket algorithms implemented against the Redis store with atomic Lua increments; concurrency verified by integration test). Fail-open on Redis outage is a deliberate, documented trade-off.
- Circuit breaker: CLOSED → OPEN → HALF-OPEN state machine enforced by a DelegatingHandler on outbound HTTP clients, state observable via `GET /api/v1/reliability/circuit-breakers`.

**Event platform**
- Kafka publish with versioned contract headers (event-type/version/correlation-id), header/payload verified by integration test; consumer with exponential-backoff retry then DLQ; DB-unique-index idempotency guard; dead-letter inspection with topic/type/time/event-id filters; dry-run and authorized live replay (`dryRun` is a query parameter, live replay is `PlatformAdmin` and audit-logged); in-flight consumer never crashes the process when Kafka is down.
- **One real end-to-end flow exists today**: recording a deployment publishes `DeploymentRecorded` to `atlas.events.deployments` *after* the deployment row is durable (a broker outage is logged and counted, never rolled back), and the consumer-side handler registered through `IEventHandlerRegistry.Register` writes an append-only audit entry per event — de-duplicated by the idempotency guard before the handler runs.

**Observability & SLO**
- OTel ASP.NET Core/HttpClient/runtime instrumentation, Npgsql span source, optional OTLP span export (`OpenTelemetry:Otlp:Endpoint`), Prometheus `/metrics`, provisioned Grafana dashboard (`observability/grafana/`), dependency-aware `/health` endpoints.
- **Real request telemetry**: a middleware records every non-static request into 1-minute `RequestTelemetryAggregate` buckets (route templates normalised, `{id}/{n}` substituted), attributed to a service via the route policy provider, flushed from an in-process bounded channel by a hosted service, retained 14 days, and exposed at `/api/v1/metrics/{summary,services,routes,series,infrastructure}` with `hasData`/`available` plus reasons. P50/P95/P99 are histogram bucket boundaries — never interpolated; dropped samples are counted and surfaced.
- SLO definitions + compliance/error-budget/burn computed by pure unit-tested math; **evidence precedence is explicit**: live request aggregates (`RequestCount > 0`) → recorded `MetricSample` rows → `hasData: false` (rendered as “No telemetry available.” rather than 0%). Probed availability flows automatically (health prober → `ISloService.RecordOutcomeAsync`); external request telemetry via `POST /api/v1/slo/samples/*`.
- `GET /api/v1/alerts` derives the active alert list at request time from registry health, SLO compliance, live breaker states, Kafka consumer lag and the dead-letter backlog — each alert carries the evidence it came from, and an empty list with `hasEvidence: true` means *checked and clear*.
- Command-center dashboard renders the real organization read model (service health split, open incidents, per-SLO compliance + remaining budget, recent deployments, dead-letter backlog, latest audit-trail entries) and shows **“No telemetry available.”** for any section without data.
- Deployment records + regression/canary analysis built from before/after sample windows; root-cause service scores deployment/health/error telemetry correlations and reports “Insufficient evidence.” when signals are absent.

**Operations console (Razor + vanilla ES modules)**
- Eleven operator pages, each server-rendered first and then refreshed from the same JSON APIs: Command Centre, Services (dependency topology from the registry joined with live traffic and the latest deployment, per-instance probing, routing-policy editor), APIs (catalog, version picker, per-route rate limit/timeout/retry configuration, API-key issue/reveal-once/revoke), Incidents, Observability, Event Pipelines (dead-letter inspection, dry-run, confirmed live replay, publisher dock), Deployments (record + regression/canary panels that print the server's outcome verbatim), Policies (rule table + data-only rule authoring), Reliability (breaker snapshot + derived alerts), AI Operations (read-tool selection and a confirmation-gated action panel), Audit (read-only ledger with filters).
- No SPA framework: `wwwroot/js/site.js` lazily imports one page module per `data-page`, components (charts, gauges, topology map, data tables, command bar) are shared, and an antiforgery token is issued into a meta tag for module-driven writes. SignalR is used where live updates matter (incident hub) with polling fallback when the browser client is unavailable.

**Incidents, policies, AI**
- Incident state machine (Detected → Investigating → Mitigating → Resolved → PostmortemComplete) with actor-attributed timeline, MTTD/MTTR from recorded timestamps, SignalR hub for authorized live updates.
- Policy engine: data-only conditions (no user code execution), immutable versions, evaluation API; RaiseAlert actions dedupe into IncidentManagement; destructive actions remain confirmation-gated.
- AI operations assistant: typed tools (`GetServiceHealth`, `GetIncidentHistory`, `GetSloStatus`, `GetRootCauseAnalysis`, action-kind `DeactivatePolicy`), guard separating read vs action tools (action tools require authorization + explicit confirmation + audit logging), Anthropic provider wired when `AI:AnthropicApiKey` is configured, deterministic evidence-only answers otherwise; “Insufficient evidence.” instead of fabricated telemetry.

## Running locally

Prerequisites: **.NET 9 SDK** and **Docker**.

```bash
cp .env.example .env              # set POSTGRES_PASSWORD (and optionally GRAFANA_ADMIN_PASSWORD)
docker compose up -d postgres redis kafka
dotnet ef database update --project src/Modules/Identity ...   # or simply:
scripts/migrate.sh                # applies every module's reviewed migrations
# optional deterministic demo org + admin user (development only):
#   Seed__Development=true Seed__AdminEmail=admin@atlas.local Seed__AdminPassword=<local-only> dotnet run --project src/Atlas.Web
dotnet run --project src/Atlas.Web
```

Then browse to `https://localhost:5001` (sign in with the seeded admin, or register via `POST /api/v1/account/register`). `docker compose up -d` (full stack) additionally gives you Prometheus (`:9090`), Grafana (`:3000`, provisioned *Prometheus* datasource + *ATLAS Platform Overview* dashboard), and Kafka UI (`:8081`).

Health probes: `/health/live` (process), `/health/ready` (PostgreSQL+Redis+Kafka), `/health` (aggregate). Prometheus scrapes `/metrics`.

## API surface (v1)

Cookie session for browsers, `X-Api-Key` for machines; all anonymous calls to `/api/**` get `401` (never an HTML redirect). Public contracts are versioned under `/api/v1/...`:

`/api/v1/account` (incl. `api-keys` list/issue/revoke) · `/api/v1/organizations` · `/api/v1/apis` (+`{apiId}/versions`, route config, target-service) · `/api/v1/services` (+`topology`, instances, dependencies, health-check) · `/api/v1/traffic` (policies, select-instance, telemetry) · `/api/v1/metrics` (summary, services, routes, series, infrastructure) · `/api/v1/alerts` · `/api/v1/reliability/circuit-breakers` · `/api/v1/events` (publish, dead-letters, replay, mark-replayed) · `/api/v1/incidents` (+transition, root-cause, postmortem, root-cause-analysis) · `/api/v1/deployments` (+canary-analysis, regression-analysis) · `/api/v1/slo` (list, compliance, samples/outcome, samples/latency) · `/api/v1/policies` (+versions, activate/deactivate, evaluate) · `/api/v1/audit` · `/api/v1/ai` (ask, actions)

List endpoints page (`page`/`pageSize`, clamped 1–200) and sort (`sortBy`/`sortDirection` against a per-resource whitelist) at the query level, before `Skip`/`Take`; an unknown sort field is a `400` ProblemDetails that lists the allowed values rather than silently returning unsorted data. Read endpoints publish Application DTOs — never EF aggregates, so `rowVersion`/`domainEvents` cannot leak onto the wire — and a write based on a stale read is a `409 CONCURRENCY_CONFLICT` ProblemDetails rather than a 500 or a silent overwrite. Full details: `docs/api.md`.

## Testing

```bash
dotnet test tests/Atlas.UnitTests/Atlas.UnitTests.csproj
dotnet test tests/Atlas.ArchitectureTests/Atlas.ArchitectureTests.csproj
# integration suites need PostgreSQL/Redis/Kafka (docker compose up -d postgres redis kafka):
dotnet test tests/Atlas.IntegrationTests/Atlas.IntegrationTests.csproj
```

Performance smoke: `tests/Atlas.PerformanceTests/atlas-smoke.js` (k6). CI runs it against the started host after the integration suites (thresholds: `http_req_failed < 1%`, all contract checks passing, `p(95) < 250 ms` on liveness) and uploads the measured summary as `k6-smoke-summary.json`. The step waits for `/health/ready` before measuring, and when the gate fails `scripts/k6-summary-annotations.py` republishes the failing checks and threshold values as run annotations, since a workflow log is not readable without repository access.

Two further gates guard the event path itself. The **ingress contract suite** (`EventContractValidatorTests`) runs the validator against hostile payloads — empty identifiers, non-object envelopes, a 257-character event type, duplicate keys and a depth bomb — in the normal deterministic `dotnet test` run, so a relaxation of the boundary fails CI in under a second without needing a container. The **idempotency suite** (`EventIdempotencyIntegrationTests`) proves against real PostgreSQL that a redelivered event is claimed once, that a released claim can be re-taken after a handler failure, that two consumers racing the same event produce exactly one claim, and that claims are scoped per consumer group. Neither is a benchmark; both are contract gates, and no number from them is published — see `docs/performance.md`.

## Known limitations (not faked)

- **Migrations are generated artefacts and must be regenerated when a model changes.** `scripts/migrate.sh` applies the committed migrations; `scripts/add-migration.sh <Name>` (or the *Generate EF migrations* workflow, which commits the result for you) produces the next one. The application never calls `EnsureCreated`, and it does not migrate automatically at startup — a deployment applies reviewed migrations as a release step, which is also what `docker compose` and the CI pipeline do. One review step is mandatory: Npgsql's generator emits an `AddColumn` for the `xmin` system column used by the concurrency tokens, and PostgreSQL rejects that operation (`42701`); the reviewed `ModelSync` migrations drop the obsolete `bytea` columns and carry a comment where the bogus operation was removed. Regenerating them means repeating that edit — the provider issue is linked in `docs/database.md`.
- **Session revocation is implemented; refresh-token rotation is not applicable today.** The console cookie is revalidated on every request against the user row (`IdentitySessionValidation`), so `POST /api/v1/account/sessions/revoke-all`, the PlatformAdmin `users/{id}/sessions/revoke-all`, and `users/{id}/deactivate|reactivate` all take effect on the next request and are audited; tickets expire after 12 hours and slide while used. There is no refresh-token grant because the platform issues no access tokens — API keys are long-lived until revoked, which is the documented model. **OAuth/OIDC federation is a marked, executable extension point**: the `Oidc` configuration section is bound and validated at startup (`Atlas.Shared.Security.OidcOptions`) so a partial configuration fails the host and a complete one logs that no handler is registered; the `OpenIdConnect` handler itself is deliberately not wired, and the exact registration is documented in `docs/security.md` + ADR-010. ATLAS does not authenticate against an external identity provider today.
- The AI completion provider is optional and off by default: without `AI:AnthropicApiKey` the assistant returns a deterministic composition of the evidence returned by its tools — the refusal behaviour ("Insufficient evidence.") and the citations are identical, only the prose differs.
- The SignalR browser client is loaded from a CDN; when it cannot load, live updates degrade to bounded polling and the header beacon shows the degraded state instead of pretending to be connected.
- Kafka stays optional: with no broker configured the publisher seam is absent rather than silently no-op; schema registry/outbox hardening for at-least-once publish under prolonged broker outage is documented in `docs/event-driven-architecture.md`.
- Policy actions that would change infrastructure (activate breaker, recommend rollback) are surfaced as alerts/incidents for an operator or an authorized AI action to execute; the evaluator itself never mutates platform state.
- Latency/least-connections routing needs per-instance gauges pushed to `POST /api/v1/traffic/telemetry`; instances with stale or missing reports are excluded from selection rather than scored with invented numbers.
- **Grafana coverage.** The provisioned `atlas-overview` dashboard charts both halves of the platform: what ATLAS does (HTTP rate, 5xx ratio, latency percentiles, throughput by route, response codes, .NET runtime) from the host's `/metrics`, and what it runs on (PostgreSQL connections/commits/buffer-hit ratio, Redis throughput/memory/hit ratio, Kafka consumer-group lag) from the `postgres-exporter`, `redis-exporter` and `kafka-exporter` services in `docker-compose.yml`, scraped by the matching jobs in `observability/prometheus.yml`. No panel guesses a metric name: every query targets an instrument the platform or its exporter actually publishes. Against a managed database or broker, point those three scrape jobs at the provider's exporter instead.

## Repository layout

```text
src/Atlas.Web/                         MVC host: thin controllers, Razor views, middleware, wwwroot (css/js modules)
src/Shared/                            contracts (IAtlasModule, IEventPublisher, ICacheService, ITenantContext, …), security, web
src/Modules/<Module>/{Domain,Application,Infrastructure,Presentation}
tests/Atlas.UnitTests/                 150 deterministic unit tests
tests/Atlas.IntegrationTests/          WebApplicationFactory suites (PostgreSQL/Redis/Kafka)
tests/Atlas.ArchitectureTests/         module boundary enforcement
docs/                                  architecture, security, threat model, DR, performance, ADRs
scripts/                               add-migration / migrate / smoke
observability/                         prometheus.yml, grafana provisioning + dashboards
.github/workflows/ci.yml               restore → build → warnaserror → unit → migrations → architecture → integration → Docker build+smoke
```

## Engineering discipline

- Migrations only (`scripts/add-migration.sh <Name>` → review → `scripts/migrate.sh`); `EnsureCreated` is never called.
- No secrets in git: `.env.example` holds placeholders; configuration comes from `appsettings*.json` + environment variables/user-secrets.
- CI never auto-deploys from PRs; Docker smoke verifies liveness, readiness, and metric export.

## License

MIT — see `LICENSE`.
