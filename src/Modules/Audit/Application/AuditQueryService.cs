using Atlas.Modules.Audit.Domain;
using Atlas.Shared.Application;
using Atlas.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Audit.Application;

public class AuditQueryService : IAuditQueryService
{
    private readonly AuditDbContext _db;
    public AuditQueryService(AuditDbContext db) => _db = db;

    public async Task<IReadOnlyList<AuditEntry>> ListAsync(Guid? organizationId, string? resourceType, string? action, int page, int pageSize, CancellationToken ct = default,
        string? sortBy = null, SortDirection sortDirection = SortDirection.Ascending)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var query = _db.Entries.AsNoTracking().AsQueryable();
        // An unscoped request (PlatformAdmin only — the controller rejects
        // organization roles without an organizationId) sees system records,
        // not a cross-tenant dump: tenant isolation holds even here.
        query = organizationId.HasValue
            ? query.Where(e => e.OrganizationId == organizationId.Value)
            : query.Where(e => e.OrganizationId == null);
        if (!string.IsNullOrWhiteSpace(resourceType)) query = query.Where(e => e.ResourceType == resourceType);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(e => e.Action == action);

        return await AuditSorting.Entries
            .Apply(query, sortBy, sortDirection)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }
}
