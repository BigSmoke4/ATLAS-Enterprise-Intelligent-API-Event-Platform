# ADR-001: Modular monolith as the deployment unit

**Status:** Accepted — implemented.

## Context
ATLAS spans thirteen business capabilities (Identity, Organizations, API
Management, Traffic Management, Service Registry, Event Platform, Reliability,
Observability, Incident Management, Deployment Intelligence, Policy Engine, AI
Operations, Audit). Microservices would multiply operational surface
(independent pipelines, tracing, schema and contract versioning) before the
domain boundaries have proven stable, and would make cross-cutting queries
(known in advance: topology, dashboards, SLO compliance) expensive.

## Decision
One deployable process, one PostgreSQL database, thirteen modules under
`src/Modules/<Name>/{Domain,Application,Infrastructure,Presentation}`. A module
may reference another module only through its **Application** interfaces or
through `src/Shared` contracts — never another module's `Infrastructure`,
`DbContext` or domain entities. Cross-module read joins that are presentation
shaped (topology, alerts) live in the composition root (`src/Atlas.Web/ReadModels`)
so that no module has to depend on three others.

The rule is executable, not aspirational: `tests/Atlas.ArchitectureTests`
(NetArchTest) fails the build if a module's Domain/Application layer references
another module's Infrastructure, or if an MVC controller references EF Core.

## Consequences
- One migration story, one connection pool, one trace graph, one deploy.
- Extraction remains possible: a module already has no compile-time dependency
  on another module's persistence, so moving one out means replacing its
  Application interfaces with HTTP/queue adapters.
- The boundaries cost discipline: features must be added inside the owning
  module, and shared contracts must stay narrow.

## Alternatives considered
- **Microservices per module** — rejected for now: no independent scaling need
  has been demonstrated, and the operational cost is immediate while the
  boundary risk is not.
- **Class libraries without enforcement** — rejected: boundaries decay without a
  failing test.
