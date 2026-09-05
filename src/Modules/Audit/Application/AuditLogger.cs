using Atlas.Modules.Audit.Domain;
using Atlas.Modules.Audit.Infrastructure;

namespace Atlas.Modules.Audit.Application;

public class AuditLogger : IAuditLogger
{
    private readonly AuditDbContext _db;
    public AuditLogger(AuditDbContext db) => _db = db;

    public async Task RecordAsync(Guid? actorUserId, string actorDisplay, Guid? organizationId, string action,
        string resourceType, string resourceId, Guid correlationId,
        string? beforeJson = null, string? afterJson = null, string? ipAddress = null,
        CancellationToken ct = default)
    {
        var entry = AuditEntry.Create(actorUserId, actorDisplay, organizationId, action, resourceType,
            resourceId, correlationId, beforeJson, afterJson, ipAddress);
        _db.Entries.Add(entry);
        await _db.SaveChangesAsync(ct);
    }
}
