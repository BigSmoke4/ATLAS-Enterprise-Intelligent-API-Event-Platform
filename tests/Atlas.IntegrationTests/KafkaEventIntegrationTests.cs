using Atlas.Modules.EventPlatform.Infrastructure;
using Atlas.Shared.Contracts;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Atlas.IntegrationTests;

public sealed class KafkaEventIntegrationTests
{
    private sealed record TestEvent(Guid EventId, string EventType, int Version, DateTimeOffset TimestampUtc,
        Guid CorrelationId, Guid? CausationId, string Producer, string OrderId) : IIntegrationEvent;

    [Fact]
    public async Task Publisher_writes_payload_and_versioned_correlation_headers_to_kafka()
    {
        var bootstrap = Environment.GetEnvironmentVariable("Kafka__BootstrapServers") ?? "localhost:9092";
        var topic = $"atlas.integration.{Guid.NewGuid():N}";
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build())
        {
            await admin.CreateTopicsAsync(new[] { new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 } });
        }

        var eventId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        await using var publisher = new KafkaEventPublisher(bootstrap, NullLogger<KafkaEventPublisher>.Instance);
        await publisher.PublishAsync(topic, new TestEvent(eventId, "IntegrationEvent", 3, DateTimeOffset.UtcNow, correlationId, null, "integration-tests", "order-1"));

        var config = new ConsumerConfig { BootstrapServers = bootstrap, GroupId = $"atlas-test-{Guid.NewGuid():N}", AutoOffsetReset = AutoOffsetReset.Earliest, EnableAutoCommit = false };
        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        var message = consumer.Consume(TimeSpan.FromSeconds(15));

        Assert.NotNull(message);
        Assert.Contains("IntegrationEvent", message!.Message.Headers.GetLastBytes("event-type") is { } type ? System.Text.Encoding.UTF8.GetString(type) : string.Empty);
        Assert.Equal("3", System.Text.Encoding.UTF8.GetString(message.Message.Headers.GetLastBytes("event-version")!));
        Assert.Equal(correlationId.ToString(), System.Text.Encoding.UTF8.GetString(message.Message.Headers.GetLastBytes("correlation-id")!));
        Assert.Contains(eventId.ToString(), message.Message.Value);
        consumer.Commit(message);
    }
}
