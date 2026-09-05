using Atlas.Shared.Contracts;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Atlas.Modules.EventPlatform.Infrastructure;

/// <summary>
/// Real IEventPublisher backed by Confluent.Kafka. Business logic never
/// touches Confluent types directly (ADR: "clean Kafka abstraction").
///
/// STATUS: implements publish; does NOT yet implement the consumer side
/// (consumer groups, retry topics, DLQ routing) — that's
/// Modules/EventPlatform/Infrastructure/KafkaEventConsumer (planned).
/// Untested against a real broker in this environment.
/// </summary>
public class KafkaEventPublisher : IEventPublisher, IAsyncDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaEventPublisher> _logger;

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

    public ValueTask DisposeAsync()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }
}
