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
| `GET /api/v1/audit?organizationId=...&resourceType=...&action=...&page=...&pageSize=...` (read-only) | `AuditController` | `IAuditQueryService` |
| `GET/POST /api/v1/deployments`, `GET .../{id}/regression-analysis` | `DeploymentsController` | `IDeploymentRegressionService` (pulls real Observability samples via `ISloService.GetSamplesAsync`, runs `RegressionAnalyzer`) |
| `POST /api/v1/traffic/select-instance`, `POST /api/v1/traffic/telemetry` | `TrafficController` | `ITrafficRoutingService` / `IInstanceTelemetryService` (real `ServiceRegistry` health via `IServiceHealthService`, real `RoutingStrategies` algorithms, per-instance gauges with a staleness window) |
| `GET /api/v1/metrics/{summary,services,routes,series,infrastructure}` | `MetricsController` | `ITelemetryQueryService` (one-minute request aggregates), `IDatabaseMetricsProbe`, `ICacheStatistics`, `ITelemetryIngestService`, `IConsumerLagService` — every response carries `hasData`/`available` plus a reason |
| `GET /api/v1/alerts?organizationId=` | `AlertsController` | `AlertReadModel` — alerts derived from registry health, SLO compliance, live breaker states, consumer lag and the dead-letter backlog |

Razor UI: eleven operator consoles, each rendered server-side from a typed
view model and then hydrated by one vanilla ES module against the same JSON
APIs listed above — `/Dashboard` (command centre), `/Services`,
`/Apis`, `/Incidents`, `/Observability`, `/Events`, `/Deployments`,
`/Policies`, `/Reliability`, `/AiOps` and `/Audit`. **Every** page sits behind
cookie authentication (`[Authorize]`), including the dashboard; unauthenticated
browsers are challenged to the sign-in page, and API callers get `401`, never
an HTML redirect. A page that has no recorded evidence for a section prints
"No telemetry available." (or the server's own reason string) rather than a
zero. Additional routes since the first cut of this document:
`POST /api/v1/account/register|login|logout|api-keys[/revoke]`,
`GET /api/v1/traffic/policies/{serviceId}` + `PUT` (tenant-checked body),
`POST /api/v1/traffic/select-configured-instance`,
`POST /api/v1/events/publish`, `POST /api/v1/events/dead-letters/{id}/replay`,
`POST /api/v1/slo/samples/outcome|latency`,
`GET /api/v1/incidents/{id}/root-cause-analysis`,
`GET /api/v1/deployments/{id}/canary-analysis`,
`GET /api/v1/reliability/circuit-breakers`,
`POST /api/v1/policies/{id}/activate|versions`,
`GET /api/v1/services/{id}/dependencies` (+`POST`).

## Now implemented (previously listed as gaps)

- **Pagination** on every list endpoint (`page`/`pageSize`, clamped 1-200
  via `Atlas.Shared.Application.Paging`) — APIManagement, ServiceRegistry,
  IncidentManagement, EventPlatform DLQ list, PolicyEngine, and
  DeploymentIntelligence all page at the database query level (`Skip`/`Take`),
  not in memory.
- **`[Authorize]`** on every controller (an anonymous request to any
  `/api/**` route returns `401`, never an HTML redirect; every Razor page
  challenges to sign-in), with state-changing actions further restricted to a
  named role policy — see docs/security.md.
- **`POST /api/v1/events/dead-letters/{id}/replay`** — real replay
  (dry-run or live), re-publishing to the original Kafka topic via
  `IEventPublisher`, restricted to `Role:PlatformAdmin`.
- **`POST /api/v1/ai/actions`** — the one path that can invoke an
  Action-kind AI tool (`DeactivatePolicy` today), gated by
  `ActionToolGuard` + `Role:PlatformAdmin` + explicit confirmation in the
  request body.

### Traffic routing and tenant scoping

- **`TrafficController`'s routing decision is per-instance.** RoundRobin,
  Weighted, Priority, Canary and BlueGreen route on real per-instance health
  from `IServiceHealthService.GetInstancesAsync`. LeastConnections and
  LatencyBased route on **reported** per-instance gauges pushed to
  `POST /api/v1/traffic/telemetry`; an instance whose report is missing or
  older than the staleness window (60 s by default) is excluded from selection
  instead of being scored with an invented value. Unit-tested, including the
  staleness/refusal path.
- **Resource-level tenant authorization**: `[Authorize(Policy =
  "SameOrganization")]` now applies to organization-scoped GET endpoints,
  including the security-engineer-only audit trail. Audit queries require an
  explicit `organizationId` for organization-scoped roles, support
  resource/action filters, and clamp pagination to 1–200. Platform admins may
  omit the organization filter for platform-wide/system audit records — see
  docs/security.md.

## Honest limitations still open

- **No API versioning beyond `v1`** — nothing to version yet; the routing
  and controller layout leaves space for a `v2` without breaking `v1`.
- **PolicyController's `evaluate` endpoint is read-only/advisory** by
  design — a matched rule's `Action` is returned in the response but
  nothing auto-executes it outside the explicit `/api/v1/ai/actions` path.
- **POST-body tenant checks are per-controller** (`CanAccess(...)` on the
  body's `organizationId`, verified by integration tests such as
  `SRE_cannot_use_a_different_organization_in_a_body_operation`), not a
  single pipeline convention — the `SameOrganization` requirement covers
  query/route values while body-based writes repeat the explicit check;
  unifying that is on the security roadmap (see docs/threat-model.md).
