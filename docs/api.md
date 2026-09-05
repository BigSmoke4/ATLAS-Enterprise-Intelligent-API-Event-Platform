# API Design

## Implemented

Every controller below is thin: it delegates to an Application-layer
interface and translates `Result`/success-flag objects into HTTP status +
`ProblemDetails`. None touch another module's `DbContext` directly —
cross-module reads go through the owning module's Application interface
(e.g. `DeploymentsController` → `IDeploymentRegressionService` →
`ISloService.GetSamplesAsync`, never `ObservabilityDbContext`).

| Route | Controller | Backing service |
|---|---|---|
| `GET/POST /api/v1/apis`, `.../{apiId}/versions`, `.../versions/{id}/routes` | `ApiManagementController` | `IApiCatalogService` |
| `GET/POST /api/v1/services`, `.../{id}/instances`, `POST /api/v1/service-instances/{id}/health-check` | `ServicesController` | `IServiceHealthService` |
| `GET/POST /api/v1/incidents`, `POST .../{id}/transition` | `IncidentsController` | `IIncidentService` |
| `POST /api/v1/slo`, `GET /api/v1/slo/{id}` | `SloController` | `ISloService` |
| `GET /api/v1/events/dead-letters`, `POST .../{id}/mark-replayed` | `EventsController` | `IDeadLetterService` |
| `POST /api/v1/ai/ask` | `AiController` | `AiOperationsAssistant` |
| `GET/POST /api/v1/policies`, `POST .../{id}/deactivate`, `POST .../evaluate` | `PolicyController` | `IPolicyManagementService` |
| `GET /api/v1/audit` (read-only) | `AuditController` | `IAuditQueryService` |
| `GET/POST /api/v1/deployments`, `GET .../{id}/regression-analysis` | `DeploymentsController` | `IDeploymentRegressionService` (pulls real Observability samples via `ISloService.GetSamplesAsync`, runs `RegressionAnalyzer`) |
| `POST /api/v1/traffic/select-instance` | `TrafficController` | `ITrafficRoutingService` (real `ServiceRegistry` health via `IServiceHealthService`, real `RoutingStrategies` algorithms) |

Razor UI: `/Services` and `/Incidents` render the same live data these APIs
serve. `/Dashboard` remains intentionally telemetry-free until
Observability's aggregation job exists.

## Now implemented (previously listed as gaps)

- **Pagination** on every list endpoint (`page`/`pageSize`, clamped 1-200
  via `Atlas.Shared.Application.Paging`) — APIManagement, ServiceRegistry,
  IncidentManagement, EventPlatform DLQ list, PolicyEngine, and
  DeploymentIntelligence all page at the database query level (`Skip`/`Take`),
  not in memory.
- **`[Authorize]`** on 10 of 11 controllers (`DashboardController` is the
  public landing page and stays open), with state-changing actions further
  restricted to a specific role policy — see docs/security.md.
- **`POST /api/v1/events/dead-letters/{id}/replay`** — real replay
  (dry-run or live), re-publishing to the original Kafka topic via
  `IEventPublisher`, restricted to `Role:PlatformAdmin`.
- **`POST /api/v1/ai/actions`** — the one path that can invoke an
  Action-kind AI tool (`DeactivatePolicy` today), gated by
  `ActionToolGuard` + `Role:PlatformAdmin` + explicit confirmation in the
  request body.

## Now implemented (previously listed as gaps)

- **`TrafficController`'s routing decision is now per-instance**, not
  aggregate. `IServiceHealthService.GetInstancesAsync` returns each
  instance's id/host/health; `TrafficRoutingService` uses real per-instance
  health for RoundRobin and Weighted. LeastConnections and LatencyBased are
  **honestly refused** with a clear reason string — ATLAS doesn't measure
  per-instance active-connection-count or latency anywhere, and returning
  fabricated zero values would make every instance look artificially tied.
  Unit-tested, including the refusal path.
- **Resource-level tenant authorization**: `[Authorize(Policy =
  "SameOrganization")]` now applied to 7 GET endpoints that take
  `organizationId` via query string — see docs/security.md.

## Honest limitations still open

- **No API versioning beyond `v1`** — nothing to version yet.
- **PolicyController's `evaluate` endpoint is read-only/advisory** by
  design — a matched rule's `Action` is returned in the response but
  nothing auto-executes it outside the explicit `/api/v1/ai/actions` path.
- **`SameOrganization` only covers query/route parameters, not POST
  bodies** — see the README's honest-gaps section for why.
