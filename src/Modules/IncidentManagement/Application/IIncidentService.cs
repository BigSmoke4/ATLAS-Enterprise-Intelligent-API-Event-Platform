using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.IncidentManagement.Application;

public interface IIncidentService
{
    Task<Result<Guid>> DeclareIncidentAsync(Guid organizationId, string title, IncidentSeverity severity,
        DateTimeOffset startedAtUtc, IEnumerable<Guid> affectedServiceIds, CancellationToken ct = default);

    Task<Result> TransitionAsync(Guid organizationId, Guid incidentId, IncidentStatus target, string note, CancellationToken ct = default);
    Task<IReadOnlyList<Incident>> GetActiveIncidentsAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default);
}
