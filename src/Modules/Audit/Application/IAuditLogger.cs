namespace Atlas.Modules.Audit.Application;

public interface IAuditLogger
{
    Task RecordAsync(Guid? actorUserId, string actorDisplay, Guid? organizationId, string action,
        string resourceType, string resourceId, Guid correlationId,
        string? beforeJson = null, string? afterJson = null, string? ipAddress = null,
        CancellationToken ct = default);
}
