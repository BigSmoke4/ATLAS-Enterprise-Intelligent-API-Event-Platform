using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atlas.Shared.Contracts;
using Atlas.Shared.Observability;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.EventPlatform.Application;

public sealed class EventProcessingCoordinator
{
    private readonly IIdempotencyGuard _idempotency;
    private readonly IEventHandlerDispatcher _dispatcher;
    private readonly IDeadLetterService _deadLetters;
    private readonly ILogger<EventProcessingCoordinator> _logger;

    public EventProcessingCoordinator(IIdempotencyGuard idempotency, IEventHandlerDispatcher dispatcher,
        IDeadLetterService deadLetters, ILogger<EventProcessingCoordinator> logger)
    { _idempotency = idempotency; _dispatcher = dispatcher; _deadLetters = deadLetters; _logger = logger; }

    public async Task ProcessAsync(string topic, string eventType, int version, Guid correlationId, string payloadJson,
        string consumerGroup, int maxRetries, TimeSpan initialBackoff, CancellationToken ct = default)
    {
        var eventId = ExtractEventId(payloadJson) ?? DeterministicGuidFrom(payloadJson);
        var attempt = 0;
        var backoff = initialBackoff;
        Exception? lastFailure = null;

        while (attempt <= maxRetries)
        {
            attempt++;
            try
            {
                EventContractValidator.ValidateJson(payloadJson);
                if (!await _idempotency.TryMarkProcessedAsync(consumerGroup, eventId, ct)) return;
                var handled = await _dispatcher.DispatchAsync(eventType, payloadJson, ct);
                if (!handled) _logger.LogWarning("No handler registered for event type {EventType}.", eventType);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastFailure = ex;
                await _idempotency.ReleaseAsync(consumerGroup, eventId, ct);
                if (attempt > maxRetries) break;
                // Counted only when a retry is actually scheduled: the final
                // failure is a dead-letter, not a retry.
                AtlasMetrics.EventRetries.Add(1,
                    new KeyValuePair<string, object?>("event_type", eventType));
                _logger.LogWarning(ex, "Event {EventId} attempt {Attempt}/{MaxRetries} failed.", eventId, attempt, maxRetries);
                await Task.Delay(backoff, ct);
                backoff = TimeSpan.FromMilliseconds(Math.Min(backoff.TotalMilliseconds * 2, TimeSpan.FromMinutes(1).TotalMilliseconds));
            }
        }

        await _deadLetters.RouteToDeadLetterAsync(topic, eventId, eventType, correlationId, payloadJson,
            lastFailure?.Message ?? "Event processing failed.", ct, version);
        AtlasMetrics.EventsDeadLettered.Add(1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("topic", topic));
    }

    private static Guid? ExtractEventId(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            var property = document.RootElement.EnumerateObject().FirstOrDefault(p => string.Equals(p.Name, "EventId", StringComparison.OrdinalIgnoreCase));
            return property.Value.TryGetGuid(out var id) && id != Guid.Empty ? id : null;
        }
        catch (JsonException) { return null; }
    }

    private static Guid DeterministicGuidFrom(string payload) => new(MD5.HashData(Encoding.UTF8.GetBytes(payload)));
}
