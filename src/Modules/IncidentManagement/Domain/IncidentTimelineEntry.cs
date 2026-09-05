using Atlas.Shared.Domain;

namespace Atlas.Modules.IncidentManagement.Domain;

public class IncidentTimelineEntry : Entity
{
    public Guid IncidentId { get; private set; }
    public string Note { get; private set; } = string.Empty;
    public DateTimeOffset AtUtc { get; private set; }

    private IncidentTimelineEntry() { }

    public static IncidentTimelineEntry Create(Guid incidentId, string note)
        => new() { IncidentId = incidentId, Note = note, AtUtc = DateTimeOffset.UtcNow };
}
