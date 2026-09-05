namespace Atlas.Modules.EventPlatform.Application;

/// <summary>
/// Resolves and invokes the correct IEventConsumer&lt;T&gt; for a raw
/// (eventType, payloadJson) pair coming off Kafka. Kept as its own
/// interface so KafkaEventConsumer doesn't need to know about specific
/// event types — modules register their consumers against this dispatcher.
/// </summary>
public interface IEventHandlerDispatcher
{
    /// <returns>true if a handler was found and ran (whether or not it threw is the caller's concern via exception propagation); false if no handler is registered for eventType.</returns>
    Task<bool> DispatchAsync(string eventType, string payloadJson, CancellationToken ct = default);
}
