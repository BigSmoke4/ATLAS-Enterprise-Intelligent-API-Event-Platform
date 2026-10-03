namespace Atlas.Shared.Contracts;

public interface IRawEventPublisher
{
    Task PublishRawAsync(string topic, string payloadJson, string eventType, int version, Guid eventId, Guid correlationId, Guid? causationId, string producer, DateTimeOffset timestampUtc, CancellationToken ct = default);
}
