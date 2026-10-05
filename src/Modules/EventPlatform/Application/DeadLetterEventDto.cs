using Atlas.Modules.EventPlatform.Domain;

namespace Atlas.Modules.EventPlatform.Application;

/// <summary>
/// HTTP contract for a dead-lettered event. The raw payload is part of the
/// contract on purpose (an operator inspects exactly what failed and the
/// replay path re-publishes it); the EF aggregate's bookkeeping is not.
/// </summary>
public sealed record DeadLetterEventDto(
    Guid Id,
    string OriginalTopic,
    Guid EventId,
    string EventType,
    int Version,
    Guid CorrelationId,
    string PayloadJson,
    string FailureReason,
    int RetryCount,
    DateTimeOffset FirstFailedAtUtc,
    DateTimeOffset LastFailedAtUtc,
    bool Replayed,
    DateTimeOffset CreatedAtUtc)
{
    public static DeadLetterEventDto From(DeadLetterEvent entry) => new(
        entry.Id,
        entry.OriginalTopic,
        entry.EventId,
        entry.EventType,
        entry.Version,
        entry.CorrelationId,
        entry.PayloadJson,
        entry.FailureReason,
        entry.RetryCount,
        entry.FirstFailedAtUtc,
        entry.LastFailedAtUtc,
        entry.Replayed,
        entry.CreatedAtUtc);
}
