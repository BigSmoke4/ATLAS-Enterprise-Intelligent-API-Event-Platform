using Atlas.Modules.Audit.Domain;
using Atlas.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Audit.Application;

public class AuditQueryService : IAuditQueryService
{
    private readonly AuditDbContext _db;
    public AuditQueryService(AuditDbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(Guid? organizationId, string? resourceType, int page, int pageSize, CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = _db.Entries.AsNoTracking().AsQueryable();
        if (organizationId.HasValue) query = query.Where(e => e.OrganizationId == organizationId.Value);
        if (!string.IsNullOrWhiteSpace(resourceType)) query = query.Where(e => e.ResourceType == resourceType);

        return await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
