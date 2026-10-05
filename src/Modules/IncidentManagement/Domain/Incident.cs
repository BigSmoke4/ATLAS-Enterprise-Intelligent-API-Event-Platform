using Atlas.Shared.Domain;

namespace Atlas.Modules.IncidentManagement.Domain;

/// <summary>
/// Enforces the real state machine: Detected -> Investigating -> Mitigating
/// -> Resolved -> PostmortemComplete. Skipping ahead or moving backward is
/// rejected, and MTTD/MTTR are computed only from the timestamps this class
/// itself records — never entered manually.
/// </summary>
public class Incident : TenantEntity
{
    private static readonly Dictionary<IncidentStatus, IncidentStatus[]> AllowedTransitions = new()
    {
        [IncidentStatus.Detected] = new[] { IncidentStatus.Investigating },
        [IncidentStatus.Investigating] = new[] { IncidentStatus.Mitigating },
        [IncidentStatus.Mitigating] = new[] { IncidentStatus.Resolved },
        [IncidentStatus.Resolved] = new[] { IncidentStatus.PostmortemComplete },
        [IncidentStatus.PostmortemComplete] = Array.Empty<IncidentStatus>(),
    };

    public string Title { get; private set; } = string.Empty;
    public IncidentSeverity Severity { get; private set; }
    public IncidentStatus Status { get; private set; } = IncidentStatus.Detected;

    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset DetectedAtUtc { get; private set; }
    public DateTimeOffset? InvestigatingAtUtc { get; private set; }
    public DateTimeOffset? MitigatingAtUtc { get; private set; }
    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    /// <summary>
    /// Who declared the incident. Attribution, not ownership: <see cref="Application.IncidentAccessPolicy"/>
    /// lets the declarer (and privileged roles) act on it.
    /// </summary>
    public Guid? DeclaredByUserId { get; private set; }

    public string? RootCause { get; private set; }
    public string? Mitigation { get; private set; }
    public string? PostmortemUrl { get; private set; }

    private readonly List<Guid> _affectedServiceIds = new();
    public IReadOnlyCollection<Guid> AffectedServiceIds => _affectedServiceIds.AsReadOnly();

    private readonly List<IncidentTimelineEntry> _timeline = new();
    public IReadOnlyCollection<IncidentTimelineEntry> Timeline => _timeline.AsReadOnly();

    private Incident() { }

    public static Incident Detect(Guid organizationId, string title, IncidentSeverity severity,
        DateTimeOffset startedAtUtc, IEnumerable<Guid> affectedServiceIds, Guid? declaredByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("Incident title is required.", nameof(title));

        var now = DateTimeOffset.UtcNow;
        var incident = new Incident
        {
            OrganizationId = organizationId,
            Title = title,
            Severity = severity,
            StartedAtUtc = startedAtUtc,
            DetectedAtUtc = now,
            DeclaredByUserId = declaredByUserId,
        };
        incident._affectedServiceIds.AddRange(affectedServiceIds);
        incident.AddTimelineEntry("Incident detected.");
        return incident;
    }

    public void TransitionTo(IncidentStatus target, string note, Guid? actorUserId = null)
    {
        if (!AllowedTransitions[Status].Contains(target))
            throw new InvalidOperationException($"Cannot transition from {Status} to {target}.");
        if (string.IsNullOrWhiteSpace(note)) throw new ArgumentException("Transition note is required.", nameof(note));

        var now = DateTimeOffset.UtcNow;
        Status = target;
        switch (target)
        {
            case IncidentStatus.Investigating: InvestigatingAtUtc = now; break;
            case IncidentStatus.Mitigating: MitigatingAtUtc = now; break;
            case IncidentStatus.Resolved: ResolvedAtUtc = now; break;
        }
        AddTimelineEntry(note, actorUserId, target.ToString());
        Touch();
    }

    public void RecordRootCause(string rootCause, string mitigation, Guid? actorUserId = null)
    {
        if (string.IsNullOrWhiteSpace(rootCause) || string.IsNullOrWhiteSpace(mitigation)) throw new ArgumentException("Root cause and mitigation are required.");
        RootCause = rootCause.Trim();
        Mitigation = mitigation.Trim();
        AddTimelineEntry($"Root cause recorded: {RootCause}", actorUserId, "root-cause");
        Touch();
    }

    public void CompletePostmortem(string postmortemUrl, Guid? actorUserId = null)
    {
        if (Status != IncidentStatus.Resolved)
            throw new InvalidOperationException("Postmortem can only be completed after the incident is Resolved.");
        if (string.IsNullOrWhiteSpace(postmortemUrl)) throw new ArgumentException("Postmortem URL is required.", nameof(postmortemUrl));
        PostmortemUrl = postmortemUrl.Trim();
        TransitionTo(IncidentStatus.PostmortemComplete, "Postmortem completed.", actorUserId);
    }

    private void AddTimelineEntry(string note, Guid? actorUserId = null, string entryType = "update") => _timeline.Add(IncidentTimelineEntry.Create(Id, note, actorUserId, entryType));

    /// <summary>Mean Time To Detect: time from actual incident start to detection. Null until detected (always true here since Detect() sets DetectedAtUtc immediately).</summary>
    public TimeSpan MeanTimeToDetect => DetectedAtUtc - StartedAtUtc;

    /// <summary>Mean Time To Resolve: time from detection to resolution. Null if not yet resolved.</summary>
    public TimeSpan? MeanTimeToResolve => ResolvedAtUtc.HasValue ? ResolvedAtUtc.Value - DetectedAtUtc : null;
}
