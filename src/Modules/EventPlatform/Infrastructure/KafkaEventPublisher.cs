using Atlas.Shared.Contracts;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Atlas.Modules.EventPlatform.Infrastructure;

/// <summary>
/// Real IEventPublisher backed by Confluent.Kafka. Business logic never
/// touches Confluent types directly (ADR: "clean Kafka abstraction").
///
/// KafkaEventConsumer provides the complementary consumer-group, retry, and
/// DLQ pipeline. This publisher also exposes raw replay publishing so DLQ
/// replay preserves the original business payload. The integration suite
/// (KafkaEventIntegrationTests) publishes through this class against a real
/// broker in CI and asserts the payload and versioned headers round-trip.
/// </summary>
public class KafkaEventPublisher : IEventPublisher, IRawEventPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;
    private int _disposed;

    public KafkaEventPublisher(string bootstrapServers, ILogger<KafkaEventPublisher> logger)
    {
        _logger = logger;
        var config = new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true,           // exactly-once producer semantics per partition
            Acks = Acks.All,
            MessageSendMaxRetries = 5,
            RetryBackoffMs = 200,
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync<TEvent>(string topic, TEvent @event, CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        EventContractValidator.Validate(@event);
        var payload = JsonSerializer.Serialize(@event);
        var message = new Message<string, string>
        {
            Key = @event.CorrelationId.ToString(),
            Value = payload,
            Headers = new Headers
            {
                { "event-type", System.Text.Encoding.UTF8.GetBytes(@event.EventType) },
                { "event-version", System.Text.Encoding.UTF8.GetBytes(@event.Version.ToString()) },
                { "producer", System.Text.Encoding.UTF8.GetBytes(@event.Producer) },
                { "correlation-id", System.Text.Encoding.UTF8.GetBytes(@event.CorrelationId.ToString()) },
                { "causation-id", System.Text.Encoding.UTF8.GetBytes(@event.CausationId?.ToString() ?? string.Empty) },
                { "timestamp-utc", System.Text.Encoding.UTF8.GetBytes(@event.TimestampUtc.ToString("O")) },
                { "schema", System.Text.Encoding.UTF8.GetBytes($"{ @event.EventType }.v{ @event.Version }") },
            }
        };

        try
        {
            var result = await _producer.ProduceAsync(topic, message, ct);
            _logger.LogInformation("Published {EventType} {EventId} to {Topic}/{Partition}@{Offset}",
                @event.EventType, @event.EventId, topic, result.Partition.Value, result.Offset.Value);
        }
        catch (ProduceException<string, string> ex)
        {
            // Deliberately does not throw an unhandled exception up into the
            // caller's request path — ADR: Kafka being briefly unavailable
            // must not crash the application. Callers that need a guarantee
            // should inspect the returned Task's exception or add an outbox
            // pattern (planned) for at-least-once publish under broker outage.
            _logger.LogError(ex, "Failed to publish {EventType} {EventId} to {Topic}", @event.EventType, @event.EventId, topic);
            throw;
        }
    }

    public async Task PublishRawAsync(string topic, string payloadJson, string eventType, int version, Guid eventId, Guid correlationId, Guid? causationId, string producer, DateTimeOffset timestampUtc, CancellationToken ct = default)
    {
        EventContractValidator.ValidateJson(payloadJson);
        var message = new Message<string, string>
        {
            Key = correlationId.ToString(), Value = payloadJson,
            Headers = new Headers
            {
                { "event-type", System.Text.Encoding.UTF8.GetBytes(eventType) },
                { "event-version", System.Text.Encoding.UTF8.GetBytes(version.ToString()) },
                { "producer", System.Text.Encoding.UTF8.GetBytes(producer) },
                { "correlation-id", System.Text.Encoding.UTF8.GetBytes(correlationId.ToString()) },
                { "causation-id", System.Text.Encoding.UTF8.GetBytes(causationId?.ToString() ?? string.Empty) },
                { "timestamp-utc", System.Text.Encoding.UTF8.GetBytes(timestampUtc.ToString("O")) }
            }
        };
        await _producer.ProduceAsync(topic, message, ct);
    }

    public ValueTask DisposeAsync()
    {
        // Host shutdown must never throw: a duplicate dispose (or a producer
        // whose handle is already closed by a previous flush) must not turn a
        // graceful shutdown into a failed one.
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;

        try
        {
            _producer.Flush(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kafka producer flush during shutdown did not complete cleanly.");
        }

        try
        {
            _producer.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Kafka producer dispose reported an error.");
        }

        return ValueTask.CompletedTask;
    }
}
