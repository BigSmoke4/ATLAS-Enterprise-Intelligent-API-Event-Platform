using Atlas.Shared.Domain;

namespace Atlas.Modules.EventPlatform.Domain;

/// <summary>
/// One row per (ConsumerGroup, EventId) that has been successfully
/// processed. A unique constraint on that pair is what actually makes
/// duplicate delivery safe — see ADR-007. A consumer must insert this
/// record in the SAME transaction as its business-logic write (or use it as
/// a compensating check beforehand) or the guarantee doesn't hold.
/// </summary>
public class IdempotencyRecord : Entity
{
    public string ConsumerGroup { get; private set; } = string.Empty;
    public Guid EventId { get; private set; }
    public DateTimeOffset ProcessedAtUtc { get; private set; }

    private IdempotencyRecord() { }

    public static IdempotencyRecord Create(string consumerGroup, Guid eventId)
        => new() { ConsumerGroup = consumerGroup, EventId = eventId, ProcessedAtUtc = DateTimeOffset.UtcNow };
}
