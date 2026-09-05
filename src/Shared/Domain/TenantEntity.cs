namespace Atlas.Shared.Domain;

/// <summary>
/// Base class for any entity that belongs to a single Organization (tenant).
/// EF global query filters MUST be applied on every DbSet of a TenantEntity
/// so that a user from Organization A can never read Organization B's rows,
/// even if the application-layer check is somehow bypassed.
/// </summary>
public abstract class TenantEntity : Entity
{
    public Guid OrganizationId { get; protected set; }
}
