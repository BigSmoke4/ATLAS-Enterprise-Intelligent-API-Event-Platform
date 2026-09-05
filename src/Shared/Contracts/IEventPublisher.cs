namespace Atlas.Shared.Contracts;

/// <summary>
/// Abstraction over the event bus (Kafka in production). Business logic in
/// modules must depend only on this interface, never on Kafka client types
/// directly, so the broker can be swapped or mocked in tests.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(string topic, TEvent @event, CancellationToken ct = default)
        where TEvent : IIntegrationEvent;
}

/// <summary>
/// Contract every cross-module event must satisfy so consumers can
/// deduplicate, trace, and order events consistently.
/// </summary>
public interface IIntegrationEvent
{
    Guid EventId { get; }
    string EventType { get; }
    int Version { get; }
    DateTimeOffset TimestampUtc { get; }
    Guid CorrelationId { get; }
    Guid? CausationId { get; }
    string Producer { get; }
}

public interface IEventConsumer<TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
