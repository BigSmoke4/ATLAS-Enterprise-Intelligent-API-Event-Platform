# ADR-002: PostgreSQL 16 with EF Core 9, migrations only

**Status:** Accepted — implemented.

## Context
The platform needs relational integrity (audit trail, incident timelines,
idempotency records, deployment history), real concurrency control, and a query
engine capable of the aggregation work SLOs and dashboards need — without a
second storage technology for "just the reports".

## Decision
One PostgreSQL database. Every module owns a `DbContext` with **its own schema**
and its own `__EFMigrationsHistory` table (for example `observability`,
`eventplatform`, `audit`), so module ownership is visible in the database and a
future extraction does not have to untangle a shared schema.

- **Migrations only, never `EnsureCreated`.** The reviewed `InitialSchema` for
  each context is committed under `src/Modules/*/Infrastructure/Migrations`;
  `scripts/migrate.sh` applies them and CI proves they apply to a real
  PostgreSQL 16 and that the model has not drifted from the committed snapshot.
- **Constraints in the database**, not only in code: unique indexes (org+base
  path, API version number, route path+method, telemetry bucket key,
  consumer-group+event id), foreign keys, and check constraints where the
  domain has an invariant.
- **Optimistic concurrency** through `RowVersion` (`Entity.RowVersion`); UUID
  keys are generated in the domain so an entity has identity before it is
  persisted.
- Reads use `AsNoTracking`, projections and database-side paging; indexes are
  added with the query that needs them (documented in `docs/database.md`).

## Consequences
- One backup/restore story (`docs/disaster-recovery.md`), one connection
  string, transactional consistency across modules when a use case genuinely
  needs it.
- Per-module schemas keep the door open for moving a module to its own database
  later, at the cost of a second migration history to manage.
- Schema changes are a review artefact, not a side effect of starting the app.

## Alternatives considered
- **`EnsureCreated()` for speed** — rejected: no upgrade path, and it hides
  schema drift until production.
- **Database-per-module from day one** — rejected: cross-module transactions and
  the composition-root reads would become distributed problems before they are
  business problems.
- **A document store for telemetry** — rejected: the aggregates are queried
  relationally (per tenant/service/route/window) and PostgreSQL already owns
  the retention job; a second store would add an operational dependency
  without changing what is stored.
