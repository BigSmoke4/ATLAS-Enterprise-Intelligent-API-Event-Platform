using Atlas.Modules.Audit.Domain;

namespace Atlas.Modules.Audit.Application;

public interface IAuditQueryService
{
    /// <summary>Page of append-only audit entries; <c>sortBy</c> must come from <see cref="AuditSorting.Entries"/>.</summary>
    Task<IReadOnlyList<AuditEntry>> ListAsync(Guid? organizationId, string? resourceType, string? action, int page, int pageSize, CancellationToken ct = default,
        string? sortBy = null, SortDirection sortDirection = SortDirection.Ascending);
}
