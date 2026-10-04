using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Modules.IncidentManagement.Infrastructure;
using Atlas.Shared.Application;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.IncidentManagement.Application;

public class IncidentService : IIncidentService
{
    private readonly IncidentManagementDbContext _db;
    public IncidentService(IncidentManagementDbContext db) => _db = db;

    public async Task<Result<Guid>> DeclareIncidentAsync(Guid organizationId, string title, IncidentSeverity severity,
        DateTimeOffset startedAtUtc, IEnumerable<Guid> affectedServiceIds, CancellationToken ct = default)
    {
        try
        {
            var incident = Incident.Detect(organizationId, title, severity, startedAtUtc, affectedServiceIds);
            _db.Incidents.Add(incident);
            await _db.SaveChangesAsync(ct);
            return Result.Success(incident.Id);
        }
        catch (ArgumentException ex) { return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR"); }
    }

    public async Task<Result> TransitionAsync(Guid organizationId, Guid incidentId, IncidentStatus target, string note, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var incident = await _db.Incidents.Include(i => i.Timeline).FirstOrDefaultAsync(i => i.Id == incidentId && i.OrganizationId == organizationId, ct);
        if (incident is null) return Result.Failure("Incident not found.", "NOT_FOUND");
        try { incident.TransitionTo(target, note, actorUserId); await _db.SaveChangesAsync(ct); return Result.Success(); }
        catch (ArgumentException ex) { return Result.Failure(ex.Message, "VALIDATION_ERROR"); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message, "INVALID_TRANSITION"); }
    }

    public async Task<Result> RecordRootCauseAsync(Guid organizationId, Guid incidentId, string rootCause, string mitigation, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var incident = await _db.Incidents.Include(i => i.Timeline).SingleOrDefaultAsync(i => i.Id == incidentId && i.OrganizationId == organizationId, ct);
        if (incident is null) return Result.Failure("Incident not found.", "NOT_FOUND");
        try { incident.RecordRootCause(rootCause, mitigation, actorUserId); await _db.SaveChangesAsync(ct); return Result.Success(); }
        catch (ArgumentException ex) { return Result.Failure(ex.Message, "VALIDATION_ERROR"); }
    }

    public async Task<Result> CompletePostmortemAsync(Guid organizationId, Guid incidentId, string postmortemUrl, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var incident = await _db.Incidents.Include(i => i.Timeline).SingleOrDefaultAsync(i => i.Id == incidentId && i.OrganizationId == organizationId, ct);
        if (incident is null) return Result.Failure("Incident not found.", "NOT_FOUND");
        try { incident.CompletePostmortem(postmortemUrl, actorUserId); await _db.SaveChangesAsync(ct); return Result.Success(); }
        catch (ArgumentException ex) { return Result.Failure(ex.Message, "VALIDATION_ERROR"); }
        catch (InvalidOperationException ex) { return Result.Failure(ex.Message, "INVALID_TRANSITION"); }
    }

    public Task<Incident?> GetAsync(Guid organizationId, Guid incidentId, CancellationToken ct = default)
        => _db.Incidents.Include(i => i.Timeline).AsNoTracking().SingleOrDefaultAsync(i => i.Id == incidentId && i.OrganizationId == organizationId, ct);

    public async Task<IReadOnlyList<Incident>> GetActiveIncidentsAsync(Guid organizationId, int page = 1, int pageSize = 50, IncidentStatus? status = null, IncidentSeverity? severity = null, CancellationToken ct = default,
        string? sortBy = null, SortDirection sortDirection = SortDirection.Ascending)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var query = _db.Incidents.Where(i => i.OrganizationId == organizationId && i.Status != IncidentStatus.PostmortemComplete);
        if (status.HasValue) query = query.Where(i => i.Status == status.Value);
        if (severity.HasValue) query = query.Where(i => i.Severity == severity.Value);
        return await IncidentSorting.Incidents.Apply(query, sortBy, sortDirection)
            .Skip((page - 1) * pageSize).Take(pageSize).AsNoTracking().ToListAsync(ct);
    }
}
