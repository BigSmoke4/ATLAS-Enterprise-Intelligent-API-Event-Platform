namespace Atlas.Modules.EventPlatform.Application;

/// <summary>
/// Call before executing a consumer's business logic for an event. If this
/// returns false, the event has already been processed by this consumer
/// group and the handler MUST skip the business operation — this is what
/// makes "deliver twice, execute once" (ADR-007) real rather than aspirational.
/// </summary>
public interface IIdempotencyGuard
{
    Task<bool> TryMarkProcessedAsync(string consumerGroup, Guid eventId, CancellationToken ct = default);
}
