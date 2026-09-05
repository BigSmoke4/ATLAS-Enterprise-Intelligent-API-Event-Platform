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
| Organizations | Real: Organization/Team/Environment entities, EF tenant query filters via `Atlas.Shared.Contracts.ITenantContext` |
| APIManagement | Real: ApiDefinition/ApiVersion/ApiRoute domain + EF persistence + `IApiCatalogService` application layer, tenant-scoped. REST endpoints not yet mapped. |
| ServiceRegistry | Real: `RegisteredService`/`ServiceInstance` with health *derived* from recorded check history (never assigned directly), EF persistence, `IServiceHealthService`. No automated health-check prober yet — health must be fed in via `RecordHealthCheckAsync`. |
| EventPlatform | Real: idempotency guard (DB unique index), a Confluent.Kafka-backed `IEventPublisher`, AND now `KafkaEventConsumer` (consumer group + exponential-backoff retry + automatic DLQ routing via `IDeadLetterService`) registered as a `BackgroundService` whenever `Kafka:Topics` is non-empty. Not implemented: operator-driven replay actually re-publishing a dead-lettered message. |
| Audit | Real: append-only `AuditEntry` log — `AuditDbContext` rejects Modified/Deleted entity states in `SaveChanges`. Not yet called by other modules' write paths. |
| Reliability | Real, unit-tested, AND wired to the live pipeline: `RedisRateLimitStore` (atomic Lua-scripted INCR/EXPIRE) backs `RateLimitingMiddleware`, enforced on every HTTP request via `app.UseAtlasRateLimiting()`; `CircuitBreakerRegistry` + `CircuitBreakerDelegatingHandler` enforce a real circuit breaker on outbound `HttpClient` calls. Per-route policy lookup (from APIManagement.ApiRoute.RateLimit) is not yet integrated — a single process-wide default limit applies today. |
| TrafficManagement | Real, unit-tested routing algorithms, wired to real PER-INSTANCE ServiceRegistry health data via `IServiceHealthService.GetInstancesAsync` (Application-interface boundary, not DbContext). RoundRobin/Weighted fully functional; LeastConnections/LatencyBased are honestly refused (ATLAS doesn't measure per-instance latency/connections) rather than fed fabricated data. |
| Observability | Real: `ServiceLevelObjective`/`MetricSample` entities, `SloCalculator` (pure, unit-tested error-budget/burn-rate math — compliance always derived from recorded samples). OpenTelemetry ASP.NET Core tracing/metrics wired; no exporter configured yet, and no background job populates samples automatically. |
| IncidentManagement | Real: `Incident` state machine (Detected→Investigating→Mitigating→Resolved→PostmortemComplete) rejects illegal/backward transitions; MTTD/MTTR computed from entity timestamps, unit-tested. No automatic incident creation from alerts yet. |
| DeploymentIntelligence | Real end-to-end: `Deployment` entity + `RegressionAnalyzer` (pure, unit-tested), AND `IDeploymentRegressionService` now pulls real before/after `MetricSample` windows from Observability via `ISloService.GetSamplesAsync` (Application-interface boundary) and runs the analyzer automatically. Exposed via `DeploymentsController`. |
| PolicyEngine | Real: safe, data-only rule representation (`PolicyCondition`: field/operator/value — no code execution) + `PolicyEvaluator`, unit-tested, versioned rules. Evaluator is read-only/advisory — nothing auto-fires an action yet (by design, pending the AI-safety confirmation flow). |
| AIOperations | Real for READ evidence-gathering: `AiOperationsAssistant` refuses to answer without tool evidence (returns "Insufficient evidence." otherwise); 3 real tools (`GetServiceHealth`, `GetIncidentHistory`, `GetSLOStatus`) call the actual Application services of their modules; `ActionToolGuard` enforces authorization+confirmation for any future action tool. Not implemented: an `IAiCompletionClient` (no LLM provider wired), so answers are a deterministic evidence summary, not fluent prose; no REST/UI endpoint yet. |

This matches the project's own "No Fake Functionality" rule: rather than
scaffold every module with canned API responses, unimplemented modules are
left as documented, empty seams.
