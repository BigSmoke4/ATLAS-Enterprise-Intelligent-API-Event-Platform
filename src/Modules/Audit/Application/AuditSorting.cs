using Atlas.Modules.Audit.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.Audit.Application;

/// <summary>Sortable fields for GET /api/v1/audit — default ordering: newest first.</summary>
public static class AuditSorting
{
    public static readonly SortSpec<AuditEntry> Entries = SortSpec<AuditEntry>.Create(
        defaultField: "createdAtUtc",
        defaultOrdering: q => q.OrderByDescending(e => e.CreatedAtUtc),
        ("createdAtUtc", (desc, q) => desc ? q.OrderByDescending(e => e.CreatedAtUtc) : q.OrderBy(e => e.CreatedAtUtc)),
        ("action", (desc, q) => desc ? q.OrderByDescending(e => e.Action) : q.OrderBy(e => e.Action)),
        ("resourceType", (desc, q) => desc ? q.OrderByDescending(e => e.ResourceType) : q.OrderBy(e => e.ResourceType)),
        ("resourceId", (desc, q) => desc ? q.OrderByDescending(e => e.ResourceId) : q.OrderBy(e => e.ResourceId)),
        ("actorDisplay", (desc, q) => desc ? q.OrderByDescending(e => e.ActorDisplay) : q.OrderBy(e => e.ActorDisplay)));
}
