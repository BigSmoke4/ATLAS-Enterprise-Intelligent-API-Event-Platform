using System.Collections.Concurrent;
using Atlas.Modules.EventPlatform.Application;

namespace Atlas.Modules.EventPlatform.Infrastructure;

/// <summary>
/// Registry + dispatcher for consumer-side handlers, keyed by event type
/// string. Modules register their IEventConsumer&lt;T&gt; here (typically in
/// their own RegisterServices) via IEventHandlerRegistry.Register — this is
/// what lets KafkaEventConsumer stay generic instead of hard-coding a
/// switch statement over every module's event types.
/// </summary>
public interface IEventHandlerRegistry
{
    void Register(string eventType, Func<string, CancellationToken, Task> handler);
}

public class EventHandlerDispatcher : IEventHandlerRegistry, IEventHandlerDispatcher
{
    private readonly ConcurrentDictionary<string, Func<string, CancellationToken, Task>> _handlers = new();

    public void Register(string eventType, Func<string, CancellationToken, Task> handler)
        => _handlers[eventType] = handler;

    public async Task<bool> DispatchAsync(string eventType, string payloadJson, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(eventType, out var handler)) return false;
        await handler(payloadJson, ct);
        return true;
    }
}
