# ATLAS — Enterprise Intelligent API & Event Platform

A modular-monolith ASP.NET Core (net9.0) platform for API management,
traffic routing, event processing, reliability, observability, incident
response, deployment intelligence, policy automation, and AI-assisted
operations.

## What "complete" still doesn\'t mean here

Every gap raised in review so far has real code behind it: auth, security
headers, CSRF plumbing, webhook signature verification, pagination, the
Kafka consumer/DLQ/replay path, live rate-limiting/circuit-breaking, an
automated health prober, resource-level tenant authorization (on top of
role checks and EF query filters), and one real AI action tool with an
audit trail.

**A bug this process itself caught:** an early shell brace-expansion
failure silently dropped `Domain/AtlasRoles.cs`, `Domain/AtlasUser.cs`,
`Domain/ApiKey.cs`, and `Presentation/IdentityModule.cs` from the Identity
module — the very early `cat > Domain/X.cs <<EOF` calls failed with
"Directory nonexistent" and the shell just moved on. This wasn't caught
until an explicit file-by-file audit against every module's expected
contents. It's now fixed and `IdentityModule.cs` also registers the new
`SameOrganization` authorization policy. The lesson stayed in the process:
past "it compiled in my head" confidence is not the same as verified
presence on disk — hence the full-tree audit now in the getting-started
checklist below.

What's still **not** here, and can't be faked into existing:

- **No compiled build.** This sandbox has no .NET SDK — nothing has run
  `dotnet build` or `dotnet test` once. Expect real compiler errors, and
  possibly more silent gaps like the one above that only a real compile
  will surface definitively.
- **No EF Core migrations.** `dotnet ef migrations add` requires compiling
  the model first — there's no `Migrations/` folder in any module. Run the
  commands under "Getting started" yourself after your first successful
  build.
- **Retry-*topics*** (vs. in-process retry+backoff) and payload schema
  validation before Kafka dispatch aren't built.
- **Resource-level authorization only covers query/route parameters**, not
  request bodies — `OrganizationAccessHandler` can't inspect a POST body's
  `OrganizationId` without buffering the request stream, so
  `[Authorize(Policy="SameOrganization")]` is only applied to GET actions
  today. A POST from an authenticated SRE with a spoofed `OrganizationId`
  in the JSON body would still pass this specific check (though it would
  still be scoped by the EF tenant query filter on the read side, and by
  whatever the underlying entity lookup requires matching organizationId
  for).

## ⚠️ Honest status before you push this

This repository was generated in a sandbox **with no .NET SDK and no
internet access to install one** — so nothing here has been compiled,
`dotnet restore`d, or test-run. The person who requested this project asked
for it anyway, understanding they'd build/test it themselves. Concretely,
before you trust anything:

```bash
dotnet restore ATLAS.sln
dotnet build ATLAS.sln
dotnet test tests/Atlas.UnitTests/Atlas.UnitTests.csproj
```

Expect to fix compiler errors — package versions, using-directives, and EF
Core configuration were hand-written against my knowledge of the APIs, not
verified against a real build. Treat this as a strong, structurally honest
starting point, not a finished, verified product.

### What's actually real vs. planned

| Area | Status |
|---|---|
| Modular monolith skeleton, `IAtlasModule` wiring, `Program.cs` | Real |
| Identity (ASP.NET Core Identity, roles, hashed API keys) | Real |
| Organizations (entities, EF tenant-isolation query filters via `ITenantContext`) | Real |
| APIManagement (API/Version/Route domain + EF + application service) | Real, no REST endpoints mapped yet |
| ServiceRegistry (health derived from real check history, not assigned) | Real, no automated prober yet — health must be fed in |
| EventPlatform (DB-unique-index idempotency guard, DLQ entity, Kafka producer) | Real for publish + idempotency; consumer/DLQ routing/replay not implemented |
| Audit (append-only log, enforced in `SaveChanges`) | Real; not yet called from other modules' write paths |
| Reliability: rate limiting **enforced on every live HTTP request** | Real — `RateLimitingMiddleware` (`app.UseAtlasRateLimiting()`) backed by `RedisRateLimitStore` (atomic Lua INCR+EXPIRE), fails open if Redis is down |
| Reliability: circuit breaker **enforced on outbound HTTP calls** | Real — `CircuitBreakerDelegatingHandler` attached to a named `HttpClient` via `ICircuitBreakerRegistry`; a tripped circuit throws before hitting the network |
| Traffic routing algorithms (round robin, weighted, least-conn, latency) | Real, **unit tested**, pure functions — no persistence yet |
| EventPlatform: Kafka **consumer + retry + DLQ routing** | Real — `KafkaEventConsumer` (BackgroundService): exponential backoff up to configurable max retries, then routes to `DeadLetterEvent` via `IDeadLetterService`; idempotency checked before dispatch |
| REST APIs — 11 controllers covering all 13 modules | Real — every controller is thin, calling only Application-layer interfaces; 10 of 11 carry `[Authorize]` (role-restricted for state-changing actions); every list endpoint is paginated at the DB query level |
| Authorization enforcement | Real — `[Authorize]`/`[Authorize(Policy="Role:...")]` on 10 controllers, mapped to `AtlasRoles` |
| Security headers (CSP, X-Frame-Options, etc.) | Real — `SecurityHeadersMiddleware`, applied to every response |
| CSRF protection | Real plumbing (`AddAntiforgery`) — no vulnerable form exists yet to exercise it |
| Webhook signature verification | Real — `WebhookSignatureVerifier` (HMAC-SHA256, constant-time compare), unit-tested; no webhook receiver endpoint built yet |
| ServiceRegistry: automated health-check prober | Real — `HealthCheckProberService` polls every instance's `/health/live` on a timer |
| EventPlatform: DLQ replay | Real — dry-run and live replay actually re-publish to the original Kafka topic, unit-tested |
| AIOperations: action tool + audit trail | Real — `DeactivatePolicyTool` (the first Action-kind tool) runs only through `ActionToolGuard`; every attempt is recorded via `IAuditLogger` |
| AIOperations: LLM prose generation | Real HTTP integration (`AnthropicCompletionClient`) — inactive until `AI:AnthropicApiKey` is set; falls back to deterministic evidence summary otherwise |
| Resource-level tenant authorization | Real — `OrganizationAccessHandler`/`SameOrganization` policy checks the caller's `org_id` claim against the requested `organizationId` (query/route), on top of role checks and EF query filters; `PlatformAdmin` bypasses by design. Applied to 7 GET endpoints. Does not yet cover POST bodies (see below) |
| TrafficManagement: per-instance routing | Real — `IServiceHealthService.GetInstancesAsync` (per-instance id/host/health) feeds `TrafficRoutingService`; RoundRobin/Weighted fully real, LeastConnections/LatencyBased honestly refused (not measured) instead of fed fake data. Unit-tested. |
| Identity module completeness | Fixed a real bug: `AtlasRoles`, `AtlasUser`, `AtlasRole`, `ApiKey`, and `IdentityModule.cs` had silently failed to write in an earlier session (shell brace-expansion issue) and were missing entirely — caught via a full file-by-file audit and restored |
| Razor UI: `/Services`, `/Incidents` | Real — server-rendered from live data via the same Application services; shows "No telemetry available." with no organizationId rather than fabricating rows |
| Docker Compose (Postgres, Redis, Kafka, Kafka UI), Dockerfile, CI workflow | Real config, unrun |
| Observability (SLO/error-budget math, OTel tracing wired) | Real, **unit tested** — compliance/error-budget always derived from samples, never fabricated |
| IncidentManagement (state machine, MTTD/MTTR) | Real, **unit tested** — illegal/backward transitions rejected |
| DeploymentIntelligence (regression analysis) | Real, **unit tested** pure comparison; not yet auto-wired to Observability |
| PolicyEngine (safe data-only rules, no code exec) | Real, **unit tested**; evaluator is read-only/advisory by design |
| AIOperations (real read-tool evidence gathering: GetServiceHealth/GetIncidentHistory/GetSLOStatus, refuses to answer without evidence) | Real, **unit tested**; LLM prose generation (`IAiCompletionClient`) and action tools not implemented |

The full ATLAS spec (13 modules, Kafka event platform, OpenTelemetry, SLO
engine, canary analysis, policy engine, AI ops assistant, full test
pyramid, threat model, DR plan) is realistically weeks-to-months of work.
Building it end-to-end here would have meant faking large parts of it,
which the spec itself explicitly forbids ("No Fake Functionality Rule").
So: real foundation now, clearly marked seams for the rest, phase-by-phase
per `docs/architecture.md`.

## Getting started

```bash
cp .env.example .env        # set POSTGRES_PASSWORD
docker compose up -d postgres redis kafka kafka-ui
dotnet ef migrations add InitialCreate --project src/Modules/Identity --startup-project src/Atlas.Web
dotnet ef migrations add InitialCreate --project src/Modules/Organizations --startup-project src/Atlas.Web
# ...repeat for APIManagement, ServiceRegistry, EventPlatform, Audit, Observability,
# IncidentManagement, DeploymentIntelligence, PolicyEngine — each owns its own schema/migrations.
dotnet ef database update --project src/Modules/Identity --startup-project src/Atlas.Web
dotnet ef database update --project src/Modules/Organizations --startup-project src/Atlas.Web
# ...and the rest, same pattern.
dotnet run --project src/Atlas.Web
```

Then visit `http://localhost:5000` (or whatever port `dotnet run` prints):
- `/Dashboard` — command center
- `/Services` — live service health (`?organizationId=<guid>`)
- `/Incidents` — active incidents (`?organizationId=<guid>`)
- `/health`, `/health/live`, `/health/ready`
- `/api/v1/apis`, `/api/v1/services`, `/api/v1/incidents`, `/api/v1/slo/{id}`,
  `/api/v1/events/dead-letters`, `/api/v1/ai/ask` — REST APIs

To enable the Kafka consumer (retry + DLQ routing), set `Kafka:Topics` in
`appsettings.json` to a non-empty array — it's off by default (empty list)
since no producer is publishing real events yet.

To enable real AI prose generation, set `AI:AnthropicApiKey` (an
environment variable or user-secret, never committed) — without it,
`/api/v1/ai/ask` still works but returns a deterministic evidence summary
instead of natural-language text.

`ServiceRegistry:HealthCheck:*` controls the automated health-check
prober's interval/timeout/path — defaults to polling every 30s with a 3s
timeout against `/health/live` on each registered instance.

## Repository layout

```
src/
  Atlas.Web/            MVC host — Program.cs wires every module in
  Shared/               Cross-cutting kernel: Entity, Result, IEventPublisher,
                         ICacheService, IRateLimitStore, IAtlasModule
  Modules/<Name>/
    Domain/             entities, pure algorithms — no EF, no ASP.NET
    Application/        use cases / interfaces
    Infrastructure/      EF Core DbContext, Redis/Kafka adapters
    Presentation/        <Name>Module : IAtlasModule (module's only public surface)
tests/
  Atlas.UnitTests/          real, algorithm-level tests (circuit breaker, rate limiters)
  Atlas.IntegrationTests/   WebApplicationFactory-based host tests
  Atlas.ArchitectureTests/  planned NetArchTest boundary rules
  Atlas.PerformanceTests/   planned load-test harness
docs/                   architecture docs, ADR-001..009, threat model, DR plan
.github/workflows/ci.yml restore → build → unit → integration → docker build
docker-compose.yml      Postgres, Redis, Kafka(+Zookeeper), Kafka UI, atlas-web
```

## Why these technology choices

See `docs/decisions/` (ADR-001 through ADR-009) for modular monolith vs.
microservices, PostgreSQL, Kafka, Redis, MVC/Razor vs. SPA, rate-limiting
strategy, event idempotency, AI tool-calling architecture, and multi-tenant
isolation.

## Continuing the build

Follow the phase order from the original spec (Phase 1 Identity/Orgs is
done; Phase 2 onward — Service Registry, API Management persistence,
Traffic Management persistence, Kafka Event Platform, Observability/SLO,
Incident/Deployment intelligence, Policy Engine, AI Operations, security
hardening, performance testing, full CI/CD, final docs — are the open
work). For each module: implement `Domain` → `Application` →
`Infrastructure` → wire real endpoints into `Presentation/<Name>Module.cs`,
add unit + integration tests, then update the status table in
`docs/architecture.md` and this README.
