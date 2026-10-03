namespace Atlas.Shared.Contracts;

public interface IAuditSink
{
    Task RecordAsync(AuditRecord record, CancellationToken ct = default);
}

public sealed record AuditRecord(Guid? ActorUserId, string ActorDisplay, Guid? OrganizationId, string Action,
    string ResourceType, string ResourceId, Guid CorrelationId, string? BeforeJson = null, string? AfterJson = null, string? IpAddress = null);
