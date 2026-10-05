using Atlas.Modules.IncidentManagement.Domain;

namespace Atlas.Modules.IncidentManagement.Application;

/// <summary>
/// HTTP contract for an incident. The API never serializes the EF aggregate
/// itself: the entity carries persistence state (row version, domain-event
/// collector, organization column) that must not become a public wire format,
/// and the timeline is an entity too, so it is projected as well. Field names
/// mirror the aggregate deliberately — this is a shape boundary, not a
/// renaming exercise.
/// </summary>
public sealed record IncidentDto(
    Guid Id,
    string Title,
    IncidentSeverity Severity,
    IncidentStatus Status,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? InvestigatingAtUtc,
    DateTimeOffset? MitigatingAtUtc,
    DateTimeOffset? ResolvedAtUtc,
    string? RootCause,
    string? Mitigation,
    string? PostmortemUrl,
    Guid? DeclaredByUserId,
    IReadOnlyList<Guid> AffectedServiceIds,
    IReadOnlyList<IncidentTimelineEntryDto> Timeline,
    TimeSpan MeanTimeToDetect,
    TimeSpan? MeanTimeToResolve,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static IncidentDto From(Incident incident) => new(
        incident.Id,
        incident.Title,
        incident.Severity,
        incident.Status,
        incident.StartedAtUtc,
        incident.DetectedAtUtc,
        incident.InvestigatingAtUtc,
        incident.MitigatingAtUtc,
        incident.ResolvedAtUtc,
        incident.RootCause,
        incident.Mitigation,
        incident.PostmortemUrl,
        incident.DeclaredByUserId,
        incident.AffectedServiceIds.ToArray(),
        incident.Timeline.Select(IncidentTimelineEntryDto.From).ToArray(),
        incident.MeanTimeToDetect,
        incident.MeanTimeToResolve,
        incident.CreatedAtUtc,
        incident.UpdatedAtUtc);
}

/// <summary>One entry of the incident timeline (an entity in the model, a value on the wire).</summary>
public sealed record IncidentTimelineEntryDto(Guid Id, string Note, string EntryType, Guid? ActorUserId, DateTimeOffset AtUtc)
{
    public static IncidentTimelineEntryDto From(IncidentTimelineEntry entry)
        => new(entry.Id, entry.Note, entry.EntryType, entry.ActorUserId, entry.AtUtc);
}
