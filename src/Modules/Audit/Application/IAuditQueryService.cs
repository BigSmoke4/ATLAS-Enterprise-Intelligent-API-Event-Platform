using Atlas.Modules.Audit.Domain;

namespace Atlas.Modules.Audit.Application;

public interface IAuditQueryService
{
    Task<IReadOnlyList<AuditEntry>> ListAsync(Guid organizationId, string? resourceType, string? action, int page, int pageSize, CancellationToken ct = default);
}
