using Atlas.Modules.EventPlatform.Application;
using Atlas.Shared.Contracts;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Modules.EventPlatform.Infrastructure;

public class KafkaConsumerOptions
{
    public string BootstrapServers { get; set; } = string.Empty;
    public string ConsumerGroup { get; set; } = "atlas-default";
    public string[] Topics { get; set; } = Array.Empty<string>();
    public int MaxRetries { get; set; } = 3;
    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(1);
}

/// <summary>
/// Real consumer-group + retry + DLQ implementation: immediate retry with
/// exponential backoff up to MaxRetries, then routes the message to
/// DeadLetterEvent via IDeadLetterService (Application layer, injected
/// per-message from a scope — Kafka consumer objects are long-lived
/// singletons, application services are scoped/EF-backed, so a scope is
/// created per message rather than per consumer instance).
///
/// Idempotency: every successfully dispatched message is recorded via
/// IIdempotencyGuard BEFORE the handler's business logic is considered
/// "done" for retry purposes — if IdempotencyGuard says already-processed,
/// the message is acknowledged (offset committed) without re-dispatching.
///
/// Runs as a BackgroundService per the master prompt's "use hosted
/// background services for event processing" requirement, and gracefully
/// stops on cancellation without crashing the host if Kafka is briefly
/// unavailable (catches ConsumeException per iteration, backs off, retries
/// the poll loop itself).
/// </summary>
public class KafkaEventConsumer : BackgroundService
{
    private static readonly TimeSpan LagReportInterval = TimeSpan.FromSeconds(15);

    private readonly KafkaConsumerOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaEventConsumer> _logger;
    private readonly IConsumerLagReporter? _lagReporter;
    private DateTimeOffset _nextLagReportAtUtc = DateTimeOffset.UtcNow;

    public KafkaEventConsumer(IOptions<KafkaConsumerOptions> options, IServiceScopeFactory scopeFactory,
        ILogger<KafkaEventConsumer> logger, IConsumerLagReporter? lagReporter = null)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _lagReporter = lagReporter;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.Topics.Length == 0)
        {
            _logger.LogWarning("KafkaEventConsumer started with no topics configured; nothing to consume.");
            return;
        }

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.ConsumerGroup,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false, // manual commit: only after the message is durably processed or dead-lettered
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.Topics);

        while (!stoppingToken.IsCancellationRequested)
        {
            ConsumeResult<string, string>? result = null;
            try
            {
                result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result is null) continue;

                await ProcessWithRetryAsync(result, stoppingToken);
                consumer.Commit(result);
                ReportLag(consumer);
            }
            catch (ConsumeException ex)
            {
                // Broker temporarily unreachable etc. — log and keep the poll
                // loop alive rather than letting the whole host crash
                // (master prompt: "the application must not crash because
                // Kafka temporarily becomes unavailable").
                _logger.LogError(ex, "Kafka consume error; will retry poll loop.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break; // graceful shutdown
            }
        }

        consumer.Close();
    }

    /// <summary>
    /// Publishes committed-offset vs high-watermark lag for the partitions this
    /// consumer actually owns. Never throws: a lag-reporting failure must not
    /// interrupt consumption.
    /// </summary>
    private void ReportLag(IConsumer<string, string> consumer)
    {
        if (_lagReporter is null || DateTimeOffset.UtcNow < _nextLagReportAtUtc) return;
        _nextLagReportAtUtc = DateTimeOffset.UtcNow + LagReportInterval;

        try
        {
            var assignment = consumer.Assignment;
            if (assignment.Count == 0)
            {
                _lagReporter.Report(Array.Empty<PartitionLag>());
                return;
            }

            var committed = consumer.Committed(assignment, TimeSpan.FromSeconds(2));
            var partitions = new List<PartitionLag>(committed.Count);

            foreach (var topicPartitionOffset in committed)
            {
                var watermark = consumer.QueryWatermarkOffsets(topicPartitionOffset.TopicPartition, TimeSpan.FromSeconds(1));
                partitions.Add(new PartitionLag(
                    topicPartitionOffset.Topic,
                    topicPartitionOffset.Partition.Value,
                    topicPartitionOffset.Offset.Value,
                    watermark.High.Value,
                    _options.ConsumerGroup));
            }

            _lagReporter.Report(partitions);
        }
        catch (Exception ex) when (ex is KafkaException or TimeoutException or InvalidOperationException)
        {
            _logger.LogDebug(ex, "Consumer lag reporting skipped for this interval.");
        }
    }

    private async Task ProcessWithRetryAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var eventType = result.Message.Headers.TryGetLastBytes("event-type", out var bytes)
            ? System.Text.Encoding.UTF8.GetString(bytes) : "unknown";
        var eventVersion = result.Message.Headers.TryGetLastBytes("event-version", out var versionBytes) &&
                           int.TryParse(System.Text.Encoding.UTF8.GetString(versionBytes), out var parsedVersion) ? parsedVersion : 1;
        Guid.TryParse(result.Message.Key, out var correlationId);

        using var scope = _scopeFactory.CreateScope();
        var coordinator = new EventProcessingCoordinator(
            scope.ServiceProvider.GetRequiredService<IIdempotencyGuard>(),
            scope.ServiceProvider.GetRequiredService<IEventHandlerDispatcher>(),
            scope.ServiceProvider.GetRequiredService<IDeadLetterService>(),
            scope.ServiceProvider.GetRequiredService<ILogger<EventProcessingCoordinator>>());
        await coordinator.ProcessAsync(result.Topic, eventType, eventVersion, correlationId, result.Message.Value,
            _options.ConsumerGroup, _options.MaxRetries, _options.InitialBackoff, ct);
    }

}
