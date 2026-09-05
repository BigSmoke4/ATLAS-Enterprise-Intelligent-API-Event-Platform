namespace Atlas.Shared.Contracts;

/// <summary>
/// Resolves the current caller's organization from the authenticated
/// principal. Lives in Shared (not Organizations) because every module's
/// DbContext needs it for tenant query filters — putting it in Organizations
/// would force every other module to reference Organizations' internals.
/// </summary>
public interface ITenantContext
{
    Guid? CurrentOrganizationId { get; }
    bool HasOrganization { get; }
}
