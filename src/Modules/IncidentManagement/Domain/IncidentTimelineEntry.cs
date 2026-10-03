using Atlas.Shared.Domain;

namespace Atlas.Modules.IncidentManagement.Domain;

public class IncidentTimelineEntry : Entity
{
    public Guid IncidentId { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public string EntryType { get; private set; } = "update";
    public Guid? ActorUserId { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }

    private IncidentTimelineEntry() { }

    public static IncidentTimelineEntry Create(Guid incidentId, string note, Guid? actorUserId = null, string entryType = "update")
        => new() { IncidentId = incidentId, Note = note, ActorUserId = actorUserId, EntryType = entryType, AtUtc = DateTimeOffset.UtcNow };
}
