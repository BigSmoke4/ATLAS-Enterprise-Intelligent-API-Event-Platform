namespace Atlas.Shared.Domain;

/// <summary>
/// Base class for all aggregate roots / entities across modules.
/// Provides identity, optimistic concurrency, and domain event capture.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>EF Core row version token used for optimistic concurrency control.</summary>
    public byte[]? RowVersion { get; protected set; }

    public DateTimeOffset CreatedAtUtc { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAtUtc { get; protected set; }

    private readonly List<IDomainEvent> _domainEvents = new();
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
    protected void Touch() => UpdatedAtUtc = DateTimeOffset.UtcNow;
}

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAtUtc { get; }
}
