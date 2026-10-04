using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.ServiceRegistry.Application;

/// <summary>Sortable fields for GET /api/v1/services — default ordering: name ascending.</summary>
public static class ServiceRegistrySorting
{
    public static readonly SortSpec<RegisteredService> Services = SortSpec<RegisteredService>.Create(
        defaultField: "name",
        defaultOrdering: q => q.OrderBy(s => s.Name),
        ("name", (desc, q) => desc ? q.OrderByDescending(s => s.Name) : q.OrderBy(s => s.Name)),
        ("environmentId", (desc, q) => desc ? q.OrderByDescending(s => s.EnvironmentId) : q.OrderBy(s => s.EnvironmentId)),
        ("createdAtUtc", (desc, q) => desc ? q.OrderByDescending(s => s.CreatedAtUtc) : q.OrderBy(s => s.CreatedAtUtc)));
}
