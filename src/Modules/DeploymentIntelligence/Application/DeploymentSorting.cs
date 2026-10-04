using Atlas.Modules.DeploymentIntelligence.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.DeploymentIntelligence.Application;

/// <summary>Sortable fields for GET /api/v1/deployments — default ordering: most recent deployment first.</summary>
public static class DeploymentSorting
{
    public static readonly SortSpec<Deployment> Deployments = SortSpec<Deployment>.Create(
        defaultField: "deployedAtUtc",
        defaultOrdering: q => q.OrderByDescending(d => d.DeployedAtUtc),
        ("deployedAtUtc", (desc, q) => desc ? q.OrderByDescending(d => d.DeployedAtUtc) : q.OrderBy(d => d.DeployedAtUtc)),
        ("version", (desc, q) => desc ? q.OrderByDescending(d => d.Version) : q.OrderBy(d => d.Version)),
        ("environment", (desc, q) => desc ? q.OrderByDescending(d => d.Environment) : q.OrderBy(d => d.Environment)),
        ("author", (desc, q) => desc ? q.OrderByDescending(d => d.Author) : q.OrderBy(d => d.Author)),
        ("status", (desc, q) => desc ? q.OrderByDescending(d => d.Status) : q.OrderBy(d => d.Status)),
        ("serviceId", (desc, q) => desc ? q.OrderByDescending(d => d.ServiceId) : q.OrderBy(d => d.ServiceId)));
}
