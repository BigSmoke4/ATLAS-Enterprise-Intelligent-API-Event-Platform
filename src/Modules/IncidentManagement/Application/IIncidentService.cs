using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.IncidentManagement.Application;

public interface IIncidentService
{
    Task<Result<Guid>> DeclareIncidentAsync(Guid organizationId, string title, IncidentSeverity severity,
        DateTimeOffset startedAtUtc, IEnumerable<Guid> affectedServiceIds, CancellationToken ct = default);

    Task<Result> TransitionAsync(Guid organizationId, Guid incidentId, IncidentStatus target, string note, Guid? actorUserId = null, CancellationToken ct = default);
    Task<Result> RecordRootCauseAsync(Guid organizationId, Guid incidentId, string rootCause, string mitigation, Guid? actorUserId = null, CancellationToken ct = default);
    Task<Result> CompletePostmortemAsync(Guid organizationId, Guid incidentId, string postmortemUrl, Guid? actorUserId = null, CancellationToken ct = default);
    /// <summary>Page of active incidents; <c>sortBy</c> must come from <see cref="IncidentSorting.Incidents"/>.</summary>
    Task<IReadOnlyList<Incident>> GetActiveIncidentsAsync(Guid organizationId, int page = 1, int pageSize = 50, IncidentStatus? status = null, IncidentSeverity? severity = null, CancellationToken ct = default,
        string? sortBy = null, SortDirection sortDirection = SortDirection.Ascending);
    Task<Incident?> GetAsync(Guid organizationId, Guid incidentId, CancellationToken ct = default);
}
