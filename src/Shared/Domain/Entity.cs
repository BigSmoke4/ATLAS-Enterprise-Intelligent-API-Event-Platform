namespace Atlas.Shared.Domain;

/// <summary>
/// Base class for all aggregate roots / entities across modules.
/// Provides identity, optimistic concurrency, and domain event capture.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>
    /// Optimistic-concurrency token. Npgsql maps a <c>uint</c> property configured with
    /// <c>IsRowVersion()</c> to PostgreSQL's <c>xmin</c> system column: the token changes on
    /// every update and no table column is created. Entities that are never updated (the
    /// append-only ledger, samples and timeline rows) explicitly ignore this property and
    /// declare no token — see the entity configuration in each module's DbContext.
    /// </summary>
    public uint RowVersion { get; protected set; }

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
