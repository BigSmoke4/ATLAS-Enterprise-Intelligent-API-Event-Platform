# Database Design

- PostgreSQL, one physical database, one schema per module
  (`identity`, `organizations`, ...) to keep module ownership visible even
  though it's a single monolith database.
- Every module owns its own `DbContext` and its own EF Core migrations
  history table (`__EFMigrationsHistory` per schema).
- `Database.EnsureCreated()` is never used — migrations only.
- Tenant isolation: every `TenantEntity` carries `OrganizationId`, and
  `OrganizationsDbContext` applies `HasQueryFilter` so a query without an
  explicit tenant match returns nothing for entities scoped to a different
  organization — enforced at the query layer, not only in application code.
- Optimistic concurrency: `RowVersion` (Postgres `xmin`-backed via EF
  `IsRowVersion()`) on entities that are updated concurrently (e.g. `ApiKey`,
  `Organization`).

## Planned (not yet created)

Actual `Migrations/` folders are not included because there is no .NET SDK
in the environment that generated this repository to run
`dotnet ef migrations add`. Run:

    dotnet ef migrations add InitialCreate --project src/Modules/Identity --startup-project src/Atlas.Web
    dotnet ef migrations add InitialCreate --project src/Modules/Organizations --startup-project src/Atlas.Web

after cloning, before first run.
