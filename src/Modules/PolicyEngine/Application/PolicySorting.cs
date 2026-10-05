using Atlas.Modules.PolicyEngine.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.PolicyEngine.Application;

/// <summary>Sortable fields for GET /api/v1/policies — default ordering: name ascending.</summary>
public static class PolicySorting
{
    public static readonly SortSpec<PolicyRule> Rules = SortSpec<PolicyRule>.Create(
        defaultField: "name",
        defaultOrdering: q => q.OrderBy(r => r.Name),
        ("name", (desc, q) => desc ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name)),
        ("isActive", (desc, q) => desc ? q.OrderByDescending(r => r.IsActive) : q.OrderBy(r => r.IsActive)),
        ("version", (desc, q) => desc ? q.OrderByDescending(r => r.Version) : q.OrderBy(r => r.Version)),
        ("createdAtUtc", (desc, q) => desc ? q.OrderByDescending(r => r.CreatedAtUtc) : q.OrderBy(r => r.CreatedAtUtc)));
}
