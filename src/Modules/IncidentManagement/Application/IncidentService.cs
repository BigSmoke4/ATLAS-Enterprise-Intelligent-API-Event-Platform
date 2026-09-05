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
        catch (ArgumentException ex)
        {
            return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR");
        }
    }

    public async Task<Result> TransitionAsync(Guid organizationId, Guid incidentId, IncidentStatus target, string note, CancellationToken ct = default)
    {
        var incident = await _db.Incidents.FirstOrDefaultAsync(i => i.Id == incidentId && i.OrganizationId == organizationId, ct);
        if (incident is null) return Result.Failure("Incident not found.", "NOT_FOUND");

        try
        {
            incident.TransitionTo(target, note);
            await _db.SaveChangesAsync(ct);
            return Result.Success();
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure(ex.Message, "INVALID_TRANSITION");
        }
    }

    public async Task<IReadOnlyList<Incident>> GetActiveIncidentsAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        return await _db.Incidents
            .Where(i => i.OrganizationId == organizationId && i.Status != IncidentStatus.PostmortemComplete)
            .OrderByDescending(i => i.DetectedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
