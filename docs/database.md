# Database Design

- PostgreSQL, one physical database, one schema per module (`identity`,
  `organizations`, `trafficmanagement`, etc.).
- Every module owns its own `DbContext` and EF migrations history table.
- `Database.EnsureCreated()` is never used.
- Tenant isolation is enforced with `OrganizationId`, EF query filters, and
  application/resource authorization.
- Optimistic concurrency uses PostgreSQL's `xmin` system column on every
  mutable aggregate — see "Optimistic concurrency" below for the mapping, the
  deliberately immutable tables, and how a conflict reaches the caller.
- High-write and operational query paths have explicit indexes for tenant,
  service, status, event time, deployment version, and resource lookup.

## Migration workflow

The repository provides reviewable scripts:

```bash
# Create a named migration for every module after reviewing model changes.
scripts/add-migration.sh InitialCreate

# Apply the reviewed migrations to the configured database.
scripts/migrate.sh
```

The scripts use `dotnet ef` with the Atlas web host and an explicit DbContext;
they do not call `EnsureCreated`. Connection strings come from normal ASP.NET
configuration/environment variables. Production should run migrations as a
controlled release step using a deployment identity with schema permissions,
then run the application with a restricted runtime identity.

Migration files are generated artifacts and are committed after review. The
repository ships one reviewed `InitialSchema` migration plus a model snapshot
per module (`src/Modules/*/Infrastructure/Migrations`); the *Generate EF
migrations* workflow exists so the next one can be produced on a machine that
has the SDK and `dotnet-ef` and committed back to the branch, and
`scripts/add-migration.sh <Name>` does the same locally.

CI applies those committed migrations to ephemeral PostgreSQL before the
integration tests, and additionally runs `dotnet ef migrations add` to prove
the model still matches the checked-in snapshot — a drift between the two
fails the pipeline instead of silently producing an unreviewed schema. The
application never migrates itself at startup: a deployment applies the review
step above with a schema-scoped identity, then runs the app with a restricted
runtime identity.

## Optimistic concurrency

`Atlas.Shared.Domain.Entity.RowVersion` is a `uint` configured with
`IsRowVersion()`. Npgsql maps that property to PostgreSQL's `xmin` system
column: no table column is created, the value changes on every update, and EF
Core adds `AND xmin = @original` to every `UPDATE`/`DELETE`, so a write based on
a stale read affects zero rows and raises `DbUpdateConcurrencyException`
(verified by `ConcurrencyTokenConfigurationTests`, which sweeps every module's
model).

- **Tokens:** `Organization`, `Team`, `Environment`, `ApiDefinition`,
  `ApiVersion`, `ApiRoute`, `ApiKey`, `RegisteredService`, `ServiceInstance`,
  `DeadLetterEvent`, `ServiceLevelObjective`, `RequestTelemetryAggregate`,
  `Incident`, `Deployment`, `PolicyRule`, `TrafficPolicyConfiguration`,
  `TrafficPolicyTarget` (17 aggregates, asserted by that test). `AtlasUser` is
  an `IdentityUser<Guid>` and keeps ASP.NET Identity's own `ConcurrencyStamp`.
- **Deliberately immutable rows have no token:** `AuditEntry` (append-only — the
  `DbContext` rejects Modified/Deleted states), `IncidentTimelineEntry`,
  `MetricSample`, `IdempotencyRecord`, `ServiceDependency`. They are inserted
  and read, never updated, so a token would only add noise.
- **What the caller sees:** `ConcurrencyConflictExceptionHandler`
  (`Atlas.Shared.Web`, registered in `Program.cs`) turns the exception into
  `409 Conflict` with a ProblemDetails body (`code: CONCURRENCY_CONFLICT` and
  the conflicting entity names) instead of a 500. It is part of the exception
  pipeline, so a request rejected earlier by authentication, anti-forgery or
  validation still receives its own status code; Development and Testing hosts
  keep the developer exception page. A conflict is not retried automatically —
  retrying a stale write would silently overwrite the newer data, which is the
  exact failure the token exists to prevent.
- **Provider caveat, handled deliberately:** the Npgsql migration generator
  emits an `AddColumn<uint>("xmin", "xid", rowVersion: true, …)` for these
  properties, and PostgreSQL rejects it at apply time (`42701: column name
  "xmin" conflicts with a system column name` — npgsql/efcore.pg#3854, open at
  the time of writing). The reviewed `ModelSync` migrations therefore contain a
  comment where that operation was removed: the token works through the system
  column, and only the obsolete `bytea` `"RowVersion"` columns are dropped.
  **A future regeneration of migrations must repeat that edit** (see the
  comment in `src/Modules/*/Infrastructure/Migrations/*_ModelSync.cs`).

## Development seed

Set:

```text
Seed__Development=true
```

only after applying migrations to a development database. This enables a
hosted seed service that creates the deterministic `atlas-demo` organization
with ID `11111111-1111-1111-1111-111111111111` and deterministic default
environment IDs. Production does not enable this setting. If the schema is
not migrated, the seed service logs an error and does not call `EnsureCreated`.
