using Atlas.Modules.Organizations.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.Organizations.Application;

/// <summary>Sortable fields for GET /api/v1/organizations (PlatformAdmin surface) — default: name ascending.</summary>
public static class OrganizationSorting
{
    public static readonly SortSpec<Organization> Organizations = SortSpec<Organization>.Create(
        defaultField: "name",
        defaultOrdering: q => q.OrderBy(o => o.Name),
        ("name", (desc, q) => desc ? q.OrderByDescending(o => o.Name) : q.OrderBy(o => o.Name)),
        ("slug", (desc, q) => desc ? q.OrderByDescending(o => o.Slug) : q.OrderBy(o => o.Slug)),
        ("isActive", (desc, q) => desc ? q.OrderByDescending(o => o.IsActive) : q.OrderBy(o => o.IsActive)),
        ("createdAtUtc", (desc, q) => desc ? q.OrderByDescending(o => o.CreatedAtUtc) : q.OrderBy(o => o.CreatedAtUtc)));
}
