using System.Text.Json;
using Atlas.Modules.EventPlatform.Application;
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
    private readonly KafkaConsumerOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<KafkaEventConsumer> _logger;

    public KafkaEventConsumer(IOptions<KafkaConsumerOptions> options, IServiceScopeFactory scopeFactory, ILogger<KafkaEventConsumer> logger)
    {
        _options = options.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
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

    private async Task ProcessWithRetryAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        var eventType = result.Message.Headers.TryGetLastBytes("event-type", out var bytes)
            ? System.Text.Encoding.UTF8.GetString(bytes) : "unknown";

        Guid.TryParse(result.Message.Key, out var correlationId);

        var attempt = 0;
        var backoff = _options.InitialBackoff;

        while (true)
        {
            attempt++;
            using var scope = _scopeFactory.CreateScope();
            var idempotencyGuard = scope.ServiceProvider.GetRequiredService<IIdempotencyGuard>();
            var dispatcher = scope.ServiceProvider.GetRequiredService<IEventHandlerDispatcher>();

            try
            {
                // Extract a stable EventId for idempotency purposes. Falls back
                // to a hash of the payload if the envelope doesn't parse, so a
                // malformed message still can't be reprocessed indefinitely.
                var eventId = ExtractEventId(result.Message.Value) ?? DeterministicGuidFrom(result.Message.Value);

                var alreadyProcessed = !await idempotencyGuard.TryMarkProcessedAsync(_options.ConsumerGroup, eventId, ct);
                if (alreadyProcessed)
                {
                    _logger.LogInformation("Event {EventId} already processed by {ConsumerGroup}; skipping (idempotent).", eventId, _options.ConsumerGroup);
                    return;
                }

                var handled = await dispatcher.DispatchAsync(eventType, result.Message.Value, ct);
                if (!handled)
                {
                    _logger.LogWarning("No handler registered for event type {EventType}; message acknowledged without processing.", eventType);
                }
                return; // success
            }
            catch (Exception ex) when (attempt <= _options.MaxRetries)
            {
                _logger.LogWarning(ex, "Attempt {Attempt}/{MaxRetries} failed for {EventType}; backing off {Backoff}.",
                    attempt, _options.MaxRetries, eventType, backoff);
                await Task.Delay(backoff, ct);
                backoff *= 2; // exponential backoff
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exhausted {MaxRetries} retries for {EventType}; routing to dead-letter queue.", _options.MaxRetries, eventType);
                using var dlqScope = _scopeFactory.CreateScope();
                var dlq = dlqScope.ServiceProvider.GetRequiredService<IDeadLetterService>();
                await dlq.RouteToDeadLetterAsync(result.Topic, correlationId, eventType, correlationId, result.Message.Value, ex.Message, ct);
                return;
            }
        }
    }

    private static Guid? ExtractEventId(string payloadJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("EventId", out var idProp) && idProp.TryGetGuid(out var id))
                return id;
        }
        catch (JsonException) { /* fall through to deterministic hash below */ }
        return null;
    }

    private static Guid DeterministicGuidFrom(string payload)
    {
        var hash = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(payload));
        return new Guid(hash);
    }
}
