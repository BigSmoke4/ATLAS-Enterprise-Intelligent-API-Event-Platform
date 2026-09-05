using Atlas.Shared.Domain;

namespace Atlas.Modules.Audit.Domain;

/// <summary>
/// Append-only from the application's perspective: there is deliberately no
/// Update/Delete method on this entity, and AuditDbContext (Infrastructure)
/// blocks Modified/Deleted EntityState in SaveChanges — see that class for
/// the actual enforcement, since a missing setter alone doesn't stop raw SQL.
/// </summary>
public class AuditEntry : Entity
{
    public Guid? ActorUserId { get; private set; }
    public string ActorDisplay { get; private set; } = string.Empty;
    public Guid? OrganizationId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public Guid CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }

    private AuditEntry() { }

    public static AuditEntry Create(Guid? actorUserId, string actorDisplay, Guid? organizationId,
        string action, string resourceType, string resourceId, Guid correlationId,
        string? beforeJson = null, string? afterJson = null, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("Action is required.", nameof(action));
        if (string.IsNullOrWhiteSpace(resourceType)) throw new ArgumentException("ResourceType is required.", nameof(resourceType));

        return new AuditEntry
        {
            ActorUserId = actorUserId,
            ActorDisplay = actorDisplay,
            OrganizationId = organizationId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            CorrelationId = correlationId,
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            IpAddress = ipAddress
        };
    }
}
