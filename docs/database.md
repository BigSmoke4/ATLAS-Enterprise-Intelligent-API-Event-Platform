# Database Design

- PostgreSQL, one physical database, one schema per module (`identity`,
  `organizations`, `trafficmanagement`, etc.).
- Every module owns its own `DbContext` and EF migrations history table.
- `Database.EnsureCreated()` is never used.
- Tenant isolation is enforced with `OrganizationId`, EF query filters, and
  application/resource authorization.
- Optimistic concurrency uses `RowVersion`/PostgreSQL `xmin` where entities
  are updated concurrently.
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
