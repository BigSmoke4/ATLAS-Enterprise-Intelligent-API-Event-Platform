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
| EventPlatform | Real: idempotency guard (DB unique index), a Confluent.Kafka-backed `IEventPublisher`, AND now `KafkaEventConsumer` (consumer group + exponential-backoff retry + automatic DLQ routing via `IDeadLetterService`) registered as a `BackgroundService` whenever `Kafka:Topics` is non-empty. Not implemented: operator-driven replay actually re-publishing a dead-lettered message. |
| Audit | Real: append-only `AuditEntry` log — `AuditDbContext` rejects Modified/Deleted entity states in `SaveChanges`. Not yet called by other modules' write paths. |
| Reliability | Real and unit-tested: atomic Redis Lua implementations for fixed-window, token-bucket, sliding-window, and leaky-bucket algorithms; route policy lookup selects the configured algorithm and scope, with a safe default for unconfigured routes. Circuit-breaker registry/handler enforce outbound state and `/api/v1/reliability/circuit-breakers` exposes read-only state. |
| TrafficManagement | PostgreSQL-backed traffic policy configuration with normalized targets, tenant-safe policy APIs, and policy-driven RoundRobin/Weighted/Priority/Canary/BlueGreen selection over real ServiceRegistry instance health. LeastConnections/LatencyBased remain honestly refused until per-instance telemetry exists. |
| Observability | Real: `ServiceLevelObjective`/`MetricSample` entities, `SloCalculator` (pure, unit-tested error-budget/burn-rate math — compliance always derived from recorded samples). OpenTelemetry ASP.NET Core tracing/metrics wired; no exporter configured yet, and no background job populates samples automatically. |
| IncidentManagement | Real: `Incident` state machine (Detected→Investigating→Mitigating→Resolved→PostmortemComplete) rejects illegal/backward transitions; MTTD/MTTR computed from entity timestamps, unit-tested. No automatic incident creation from alerts yet. |
| DeploymentIntelligence | Real end-to-end: `Deployment` entity + `RegressionAnalyzer` (pure, unit-tested), AND `IDeploymentRegressionService` now pulls real before/after `MetricSample` windows from Observability via `ISloService.GetSamplesAsync` (Application-interface boundary) and runs the analyzer automatically. Exposed via `DeploymentsController`. |
| PolicyEngine | Real: safe, data-only rule representation (`PolicyCondition`: field/operator/value — no code execution) + `PolicyEvaluator`, unit-tested, versioned rules. Evaluator is read-only/advisory — nothing auto-fires an action yet (by design, pending the AI-safety confirmation flow). |
| AIOperations | Real for READ evidence-gathering: `AiOperationsAssistant` refuses to answer without tool evidence (returns "Insufficient evidence." otherwise); 3 real tools (`GetServiceHealth`, `GetIncidentHistory`, `GetSLOStatus`) call the actual Application services of their modules; `ActionToolGuard` enforces authorization+confirmation for any future action tool. Not implemented: an `IAiCompletionClient` (no LLM provider wired), so answers are a deterministic evidence summary, not fluent prose; no REST/UI endpoint yet. |

The MVC host now has cookie authentication wiring, centralized ProblemDetails
support, a reusable control-room asset pipeline, and executable NetArchTest
rules for the most important boundary constraints. The dashboard renders
explicit evidence-required states rather than synthetic numbers.

This matches the project's own "No Fake Functionality" rule: rather than
scaffold every module with canned API responses, unimplemented modules are
left as documented, empty seams. The authoritative remaining work is listed
in the repository README; CI runs build, unit, architecture, integration,
and dependency-audit stages.
