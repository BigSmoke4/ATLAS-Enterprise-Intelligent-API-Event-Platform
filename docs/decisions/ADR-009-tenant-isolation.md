# ADR-009: Multi-Tenant Isolation Strategy
Defense in depth: (1) every tenant-scoped entity inherits `TenantEntity`
with an `OrganizationId`, (2) EF Core global query filters enforce it at
the database-query layer via `ITenantContext`, (3) application services
additionally validate the resource's OrganizationId against the caller's
claim before mutating — so a bug in one layer doesn't silently expose
cross-tenant data.
