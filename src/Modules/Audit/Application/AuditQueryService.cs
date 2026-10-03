using Atlas.Modules.Audit.Domain;
using Atlas.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Audit.Application;

public class AuditQueryService : IAuditQueryService
{
    private readonly AuditDbContext _db;
    public AuditQueryService(AuditDbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(Guid organizationId, string? resourceType, string? action, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = _db.Entries.AsNoTracking().AsQueryable();
        query = query.Where(e => e.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(resourceType)) query = query.Where(e => e.ResourceType == resourceType);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(e => e.Action == action);

        return await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
