using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.IncidentManagement.Application;

/// <summary>Sortable fields for GET /api/v1/incidents — default ordering: most recently detected first.</summary>
public static class IncidentSorting
{
    public static readonly SortSpec<Incident> Incidents = SortSpec<Incident>.Create(
        defaultField: "detectedAtUtc",
        defaultOrdering: q => q.OrderByDescending(i => i.DetectedAtUtc),
        ("detectedAtUtc", (desc, q) => desc ? q.OrderByDescending(i => i.DetectedAtUtc) : q.OrderBy(i => i.DetectedAtUtc)),
        ("startedAtUtc", (desc, q) => desc ? q.OrderByDescending(i => i.StartedAtUtc) : q.OrderBy(i => i.StartedAtUtc)),
        ("severity", (desc, q) => desc ? q.OrderByDescending(i => i.Severity) : q.OrderBy(i => i.Severity)),
        ("status", (desc, q) => desc ? q.OrderByDescending(i => i.Status) : q.OrderBy(i => i.Status)),
        ("title", (desc, q) => desc ? q.OrderByDescending(i => i.Title) : q.OrderBy(i => i.Title)));
}
