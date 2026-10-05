# ADR-009: Tenant isolation in depth

**Status:** Accepted — implemented.

## Context
One deployment serves many organizations. A leak here is the most serious defect
the platform can have, so it cannot depend on every developer remembering a
`WHERE OrganizationId = @org` clause — and it must hold for the Razor pages as
well as the JSON APIs.

## Decision
Three independent layers, each of which alone would stop the common mistake:

1. **Query layer — EF Core global query filters.** Every tenant-owned entity
   inherits `TenantEntity`; each `DbContext` applies a filter driven by
   `ITenantContext`, so a query that forgets the predicate still returns only
   the current organization's rows. Background services that legitimately span
   tenants run without a tenant context, which the filter treats as
   "unfiltered by design" — an explicit, documented exception rather than a
   silent one.
2. **Authorization layer — `SameOrganization` policy.** An organization-scoped
   request may only name the caller's own organization (`org_id` claim). Every
   controller uses it on organization-scoped reads; `PlatformAdmin` is the only
   role allowed to span organizations, and that bypass is documented and tested
   rather than implied.
3. **Composition layer — explicit checks.** Operations that take an organization
   in the **body** (policy creation, traffic policy updates, key issuance)
   repeat the check against the claim, because a query filter cannot see a
   request body. MVC pages resolve the caller's organization from the claim
   (`OrganizationScopeResolver`) and return `403` for an explicit foreign
   organization.

The audit trail is append-only and always organization-stamped; unscoped audit
reads return system records only, never a cross-tenant feed.

## Consequences
- Cross-tenant reads are `403`, not silently empty: `MvcPageAuthorizationTests`
  and `AuthenticatedAuthorizationTests` cover pages, APIs and body operations.
- The `org_id` claim becomes security-relevant, so the identity pipeline (cookie
  and API-key authentication) must always stamp it — verified in the claims
  principal factory and the API-key handler.
- Combining EF filters with explicit checks means a bug must occur in two layers
  before data crosses a tenant boundary.

## Alternatives considered
- **A single query-filter layer** — rejected: it does not cover request bodies,
  route values or pages, where the leak would actually happen.
- **Database-per-tenant** — rejected as the default: it solves isolation by
  multiplying operations, and does not help a single platform-managed schema
  where roles are the boundary.
