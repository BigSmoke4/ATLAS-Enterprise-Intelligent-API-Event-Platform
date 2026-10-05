using Atlas.Modules.Audit.Domain;

namespace Atlas.Modules.Audit.Application;

/// <summary>
/// HTTP contract for one audit record. The ledger's before/after JSON is
/// exposed (that is the point of an audit trail); the row version and any
/// future storage detail are not.
/// </summary>
public sealed record AuditEntryDto(
    Guid Id,
    Guid? ActorUserId,
    string ActorDisplay,
    Guid? OrganizationId,
    string Action,
    string ResourceType,
    string ResourceId,
    string? BeforeJson,
    string? AfterJson,
    Guid CorrelationId,
    string? IpAddress,
    DateTimeOffset CreatedAtUtc)
{
    public static AuditEntryDto From(AuditEntry entry) => new(
        entry.Id,
        entry.ActorUserId,
        entry.ActorDisplay,
        entry.OrganizationId,
        entry.Action,
        entry.ResourceType,
        entry.ResourceId,
        entry.BeforeJson,
        entry.AfterJson,
        entry.CorrelationId,
        entry.IpAddress,
        entry.CreatedAtUtc);
}
