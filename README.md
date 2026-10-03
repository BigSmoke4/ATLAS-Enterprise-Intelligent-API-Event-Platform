# ATLAS — Enterprise Intelligent API & Event Platform

ATLAS is an evidence-first **ASP.NET Core MVC/Razor modular monolith** for API management, service health, event processing, reliability, observability, incident response, deployment intelligence, policy automation, and AI-assisted operations. It is intentionally not a dashboard mock: telemetry and operational conclusions are only shown when backed by stored data.

> **Build status:** the repository contains a working foundation and real implementations of the core domain algorithms and infrastructure seams. It is not honest to claim that every item in the master prompt is complete. Remaining gaps are explicitly documented below and in `docs/architecture.md`.

## Technology and boundaries

- .NET 9, nullable C#, ASP.NET Core MVC and Razor Views (no SPA framework)
- PostgreSQL/EF Core with a separate schema and DbContext per module
- Redis abstractions plus atomic Redis-backed fixed-window, token-bucket, sliding-window, and leaky-bucket request limiters
- Kafka publisher/consumer abstraction with retry, idempotency, and DLQ plumbing
- OpenTelemetry ASP.NET Core/runtime instrumentation, Prometheus `/metrics`, structured Serilog logging, and health endpoints
- xUnit tests, WebApplicationFactory integration coverage, and NetArchTest boundary checks
- One deployable process; `src/Modules/*` communicate through shared contracts/application interfaces, never another module's DbContext or infrastructure

## Implemented capabilities

- Tenant-scoped organizations and environments with EF query filters, organization/team REST use cases, and resource authorization
- ASP.NET Core Identity registration/login/logout, RBAC policies, secure password settings, lockout configuration, one-time raw API-key creation/revocation, hashed API-key storage/authentication, and cookie authentication wiring
- Service registry with persisted instances, normalized dependency edges, health derived from recorded checks, background HTTP health probing, and topology REST reads
- API/version/route registration use cases with validation, tenant scoping, route policy configuration, and Reliability integration through a shared policy-provider contract
- PostgreSQL-backed traffic policies with round-robin, weighted, priority, canary, and blue/green configuration over real healthy registered instances; unsupported telemetry-dependent strategies refuse safely rather than fabricate results
- Distributed fixed-window rate limiting using an atomic Redis Lua operation, with an explicit fail-open policy documented in the code
- Outbound circuit breaker state machine and delegating handler
- Kafka publish/consume abstraction, duplicate protection via a database uniqueness constraint, exponential retry, dead-letter persistence, and replay service
- Incident state machine with MTTD/MTTR calculations; SLO/error-budget and deployment regression algorithms derived from stored samples
- Safe policy representation (data-only predicates, no user code execution) and evidence-gated AI tool architecture with action confirmation/audit seams
- Centralized security headers, CSRF services, HMAC webhook verification, ProblemDetails support, Docker Compose, CI build/test/docker pipeline, and deterministic unit tests
- A restrained skeuomorphic control-room shell with centralized CSS/ES modules and explicit “No telemetry available” states

## Known gaps — not faked

The following are deliberately not represented as complete: refresh-token rotation/session revocation and production integration coverage for the new account endpoints, EF migrations and deterministic demo seeding, automatic telemetry aggregation, full API endpoint coverage for every module, dependency topology persistence/visualization, production Kafka schema validation/outbox guarantees, Grafana dashboard provisioning, a real LLM provider contract with tenant-safe tool execution, and load/performance results. See `docs/architecture.md`, `docs/database.md`, `docs/observability.md`, and `docs/threat-model.md` for the implementation boundary and next steps.

## Run locally

Prerequisites: .NET 9 SDK and Docker.

```bash
cp .env.example .env
# Set POSTGRES_PASSWORD to a local-only value.
docker compose up -d postgres redis kafka kafka-ui
dotnet restore ATLAS.sln
dotnet build ATLAS.sln -c Release
dotnet test tests/Atlas.UnitTests/Atlas.UnitTests.csproj -c Release
dotnet test tests/Atlas.IntegrationTests/Atlas.IntegrationTests.csproj -c Release
dotnet run --project src/Atlas.Web
```

Endpoints: `/health/live` is process liveness; `/health/ready` is reserved for dependency readiness; `/health` is the aggregate health endpoint. REST resources use `/api/v1/...`. Migrations are intentionally not run with `EnsureCreated`; create and apply reviewed EF migrations per module before using PostgreSQL in a deployment.

## Repository layout

```text
src/Atlas.Web/                 MVC host, controllers, Razor views, centralized assets
src/Modules/<Module>/{Domain,Application,Infrastructure,Presentation}
src/Shared/                    contracts, tenant context, security, cross-cutting primitives
tests/Atlas.UnitTests/         domain and application tests
tests/Atlas.IntegrationTests/ WebApplicationFactory tests
tests/Atlas.ArchitectureTests/compiled module boundary tests
docs/                          architecture, security, operations, ADRs
.github/workflows/ci.yml       restore, build, test, and Docker build
```

## Delivery discipline

The master build prompt requires compile/test verification after every phase and forbids fabricated functionality. This checkout has no .NET SDK available in the current Arena runtime, so local compilation could not be executed during this change; CI is configured to run the authoritative build and tests. Before production use, run the full migration, integration, security, and load-test review rather than treating the current foundation as a production deployment.
