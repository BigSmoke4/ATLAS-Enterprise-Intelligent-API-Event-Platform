# ATLAS Architecture

ATLAS is a **modular monolith**: one ASP.NET Core process, one PostgreSQL
database, one deployable unit — but with strict module boundaries so any
module could later be extracted into its own service.

## Layering (per module)

Razor View → MVC Controller → Application Service → Domain → Infrastructure
→ PostgreSQL / Redis / Kafka

Controllers are thin: they call an Application-layer use case and translate
the `Result`/`Result<T>` into an HTTP response. No business logic lives in
`Atlas.Web`.

## Module boundary rule

A module may only be referenced by another module through:
- `Atlas.Shared.Contracts` interfaces (e.g. `IEventPublisher`, `IAtlasModule`)
- DTOs
- domain/integration events

No module may reference another module's `Domain`, `Infrastructure`, or its
`DbContext` directly. `tests/Atlas.ArchitectureTests` is where this is
enforced mechanically (see that project — currently a placeholder for
NetArchTest-based rules, since this environment has no .NET SDK to author
and run them against real compiled assemblies).

## Current implementation status

| Module | Status |
|---|---|
| Identity | Real: ASP.NET Core Identity users/roles, hashed API keys, DbContext |
| Organizations | Real: Organization/Team/Environment entities, application service and REST operations, EF tenant query filters via `Atlas.Shared.Contracts.ITenantContext` |
| APIManagement | Real: API/version/route domain + EF persistence, tenant-scoped REST operations, route policy configuration, and a shared read-only route-policy provider consumed by Reliability. |
| ServiceRegistry | Real: services/instances with health derived from recorded check history, normalized service-dependency edges, EF persistence, automated health probing, and `IServiceHealthService` application operations. |
| EventPlatform | Real: validated versioned metadata, correlation/causation Kafka headers, a testable idempotent processing coordinator with failed-attempt claim release, consumer retry/backoff, DLQ persistence, filtered inspection, and dry-run/live raw-payload replay. |
| Audit | Real: append-only `AuditEntry` log — `AuditDbContext` rejects Modified/Deleted entity states in `SaveChanges`. Written by the DLQ operations (`DeadLetterService` via `IAuditSink`), by the Kafka event handler for consumed platform events, and by `PolicyController` for policy operations; read through `IAuditQueryService`/`AuditController`. |
| Reliability | Real and unit-tested: atomic Redis Lua implementations for fixed-window, token-bucket, sliding-window, and leaky-bucket algorithms; route policy lookup selects the configured algorithm and scope, with a safe default for unconfigured routes. Circuit-breaker registry/handler enforce outbound state and `/api/v1/reliability/circuit-breakers` exposes read-only state. |
| TrafficManagement | PostgreSQL-backed traffic policy configuration with normalized targets, tenant-safe policy APIs, and policy-driven RoundRobin/Weighted/Priority/LeastConnections/LatencyBased/Canary/BlueGreen selection over real ServiceRegistry instance health; telemetry-driven strategies consume reported per-instance gauges (IInstanceTelemetryService, POST /api/v1/traffic/telemetry) inside a staleness window, excluding unreported/stale instances honestly. |
| Observability | Real: `ServiceLevelObjective`/`MetricSample` entities, version-attributed sample recording, SLO/error-budget math, OpenTelemetry instrumentation, and Prometheus export, and file-provisioned Grafana dashboard under `observability/grafana/`. Automatic (scheduled) metric aggregation remains deployment work. |
| IncidentManagement | Real: state machine with actor-attributed timeline entries, filtered/paginated REST reads, root-cause/mitigation and postmortem operations, MTTD/MTTR computed from entity timestamps, and authorization-protected SignalR incident updates. Automatic incident creation from alerts/SLO breaches remains a PolicyEngine integration. |
| DeploymentIntelligence | Real end-to-end: deployments, before/after regression analysis, version-attributed canary analysis, evidence-based pass/fail and rollback recommendation. `DeploymentsController` exposes both analyses; no destructive rollback is automatic. |
| PolicyEngine | Real: safe data-only predicates, immutable version history, activation/deactivation audit operations, and deduplicated `RaiseAlert` integration through a shared IncidentManagement contract. Circuit-breaker/rollback actions remain advisory and confirmation-gated. |
| AIOperations | Real evidence-gathering plus root-cause analysis: read tools use actual module application services, RCA correlates incident, deployment, health, error, and latency data with explainable scoring, and returns `Insufficient evidence.` when observations are missing. Action tools remain authorization/confirmation gated. |

The MVC host now has cookie authentication wiring, centralized ProblemDetails
support, a reusable control-room asset pipeline, and executable NetArchTest
rules for the most important boundary constraints. The dashboard renders
explicit evidence-required states rather than synthetic numbers.

This matches the project's own "No Fake Functionality" rule: rather than
scaffold every module with canned API responses, unimplemented modules are
left as documented, empty seams. The authoritative remaining work is listed
in the repository README; CI runs build, unit, architecture, integration,
and dependency-audit stages.
