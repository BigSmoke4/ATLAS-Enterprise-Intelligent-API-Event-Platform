# ATLAS — Enterprise Intelligent API & Event Platform

[![CI](https://github.com/BigSmoke4/ATLAS-Enterprise-Intelligent-API-Event-Platform/actions/workflows/ci.yml/badge.svg)](https://github.com/BigSmoke4/ATLAS-Enterprise-Intelligent-API-Event-Platform/actions/workflows/ci.yml)

ATLAS is an evidence-first **ASP.NET Core 9 MVC/Razor modular monolith** for API management, service health, event processing, reliability engineering, observability, incident response, deployment intelligence, policy automation, and AI-assisted operations. One deployable process, one PostgreSQL database, strict module boundaries enforced by executable architecture tests — designed so any module can later be extracted into its own service.

The platform's defining rule is the **No Fake Functionality** contract: metrics, SLO compliance, health states, AI answers, and root-cause findings are rendered only when derived from recorded data. Where a capability is planned rather than implemented, the UI, APIs, and this README say so explicitly instead of showing a synthetic value.

## Status & verification

The full delivery pipeline is green and verifies the application end-to-end on every push:

- **Restore + Build + Static analysis** (`dotnet build -warnaserror`) — nullable-enabled C# across 18 projects.
- **115 unit tests** — routing strategies incl. telemetry-driven selection and staleness, sliding/token-bucket/leaky-bucket + fixed-window rate limiting, circuit-breaker state machine, policy evaluator + versioning, SLO/error-budget math, canary/regression analyzers, root-cause scoring, incident state machine + MTTD/MTTR, organizations/tenant rules, API-key hashing, webhook HMAC verification, event contract validation, event dispatcher/coordinator, dead-letter service, audit query service, organization-access handler.
- **EF Core migrations generated and applied** against a real PostgreSQL 16 in CI (11 module schemas; `EnsureCreated` is never used).
- **Architecture tests** (NetArchTest) — Domain/Application layers of one module may not depend on another module's Infrastructure; MVC controllers cannot reference EF Core.
- **Integration tests** (WebApplicationFactory + real PostgreSQL/Redis/Kafka): anonymous rejection with 401, unauthenticated liveness, organization isolation (403 for cross-tenant reads), role enforcement (403 for non-admin policy operations), tenant-scoped audit reads, dependency-aware readiness, atomic Redis rate-limit counters under concurrency, Kafka publisher headers/payload round-trip.
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

Modules (`src/Modules/*`): Identity, Organizations, APIManagement, TrafficManagement, ServiceRegistry, EventPlatform, Reliability, Observability, IncidentManagement, DeploymentIntelligence, PolicyEngine, AIOperations, Audit. Each has `Domain / Application / Infrastructure / Presentation` folders. Cross-module calls go only through Application interfaces or `src/Shared` contracts — never another module's DbContext or infrastructure. Design rationale lives in `docs/decisions/ADR-001…009`.

## What is implemented (verified)

**Platform & tenancy**
- ASP.NET Core Identity (email/password), lockout, secure password policy, cookie + API-key auth, role RBAC (`PlatformAdmin`, `OrganizationAdmin`, `SRE`, `Developer`, `SecurityEngineer`, `Operator`, `Viewer`), one-time-show API keys stored hashed (prefix + SHA-256), roles seeded idempotently at startup.
- Tenant isolation at three layers: EF Core global query filters keyed off `ITenantContext`, the `SameOrganization` authorization requirement (query/route org must match the `org_id` claim; PlatformAdmin bypass documented), and per-controller checks — enforced uniformly across JSON APIs **and** the Razor pages (`/Apis`, `/Services`, `/Incidents`, `/Observability` resolve the caller's org from their claim and 403 on explicit foreign organizations; AI tool access is tenant-checked), covered by integration tests (cross-tenant reads return 403).
- Append-only audit trail (DbContext rejects UPDATE/DELETE), before/after JSON, read API gated to `AuditRead` (SecurityEngineer/PlatformAdmin); unscoped reads return system records only.

**API & traffic management, reliability**
- API/version/route catalog with tenant scoping and route policy configuration (rate limit scope/algorithm/window/timeout), consumed by enforcement through the `IRoutePolicyProvider` contract.
- Traffic policies (round-robin, weighted, priority, least-connections, latency-based, canary, blue/green) routing across **real healthy registered instances**; least-connections and latency-based strategies run on per-instance gauges reported via `POST /api/v1/traffic/telemetry` (active connections / average latency), trusted only inside a staleness window — instances with stale or missing reports are excluded, never fed fabricated inputs.
- Distributed rate limiting on the live pipeline (fixed window default; token bucket, sliding window, and leaky bucket algorithms implemented against the Redis store with atomic Lua increments; concurrency verified by integration test). Fail-open on Redis outage is a deliberate, documented trade-off.
- Circuit breaker: CLOSED → OPEN → HALF-OPEN state machine enforced by a DelegatingHandler on outbound HTTP clients, state observable via `GET /api/v1/reliability/circuit-breakers`.

**Event platform**
- Kafka publish with versioned contract headers (event-type/version/correlation-id), header/payload verified by integration test; consumer with exponential-backoff retry then DLQ; DB-unique-index idempotency guard; dead-letter inspection with topic/type/time/event-id filters; dry-run and authorized live replay; in-flight consumer never crashes the process when Kafka is down.

**Observability & SLO**
- OTel ASP.NET Core/HttpClient/runtime instrumentation, Npgsql span source, optional OTLP span export (`OpenTelemetry:Otlp:Endpoint`), Prometheus `/metrics`, provisioned Grafana dashboard (`observability/grafana/`), dependency-aware `/health` endpoints.
- SLO definitions + compliance/error-budget/burn computed by pure unit-tested math from `MetricSample` rows; **probed availability telemetry flows automatically** (health prober → `ISloService.RecordOutcomeAsync`), external request telemetry via `POST /api/v1/slo/samples/*`.
- Command-center dashboard renders the real organization read model (service health split, open incidents, per-SLO compliance + remaining budget, recent deployments, dead-letter backlog, latest audit-trail entries) and shows **“No telemetry available.”** for any section without data.
- Deployment records + regression/canary analysis built from before/after sample windows; root-cause service scores deployment/health/error telemetry correlations and reports “Insufficient evidence.” when signals are absent.

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

`/api/v1/account` · `/api/v1/organizations` · `/api/v1/apis` (+route config) · `/api/v1/services` (+instances, dependencies, health-check) · `/api/v1/traffic` (policies, select-instance, telemetry) · `/api/v1/reliability/circuit-breakers` · `/api/v1/events` (publish, dead-letters, replay) · `/api/v1/incidents` (+transition, root-cause, postmortem, root-cause-analysis) · `/api/v1/deployments` (+canary-analysis, regression-analysis) · `/api/v1/slo` (+samples/outcome, samples/latency, compliance) · `/api/v1/policies` (+versions, activate/deactivate, evaluate) · `/api/v1/audit` · `/api/v1/ai` (ask, actions)

Full details: `docs/api.md`.

## Testing

```bash
dotnet test tests/Atlas.UnitTests/Atlas.UnitTests.csproj
dotnet test tests/Atlas.ArchitectureTests/Atlas.ArchitectureTests.csproj
# integration suites need PostgreSQL/Redis/Kafka (docker compose up -d postgres redis kafka):
dotnet test tests/Atlas.IntegrationTests/Atlas.IntegrationTests.csproj
```

Performance smoke: `tests/Atlas.PerformanceTests/atlas-smoke.js` (k6) — run it explicitly; results are never fabricated into the repo.

## Known limitations (not faked)

- Refresh-token rotation/session revocation, OAuth/OIDC federation: planned (cookie + API-key flows are implemented and verified today).
- Kafka stays optional: with no broker configured the publisher seam is absent rather than silently no-op; production schema registry/outbox hardening is documented in `docs/event-driven-architecture.md`.
- Automatic request-level telemetry ingestion is intentionally scoped to probed availability + explicit sample pushes until service/tenant attribution of middleware sampling is unambiguous (`docs/observability.md`).
- Grafana panels for Kafka/PostgreSQL internals (need their exporters) and browser pages beyond Command Center/APIs/Services/Incidents/Observability/Account remain roadmap items listed in `docs/architecture.md`.

## Repository layout

```text
src/Atlas.Web/                         MVC host: thin controllers, Razor views, middleware, wwwroot (css/js modules)
src/Shared/                            contracts (IAtlasModule, IEventPublisher, ICacheService, ITenantContext, …), security, web
src/Modules/<Module>/{Domain,Application,Infrastructure,Presentation}
tests/Atlas.UnitTests/                 104 deterministic unit tests
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
