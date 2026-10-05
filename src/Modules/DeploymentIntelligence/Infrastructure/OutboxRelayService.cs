using Atlas.Modules.DeploymentIntelligence.Domain;
using Atlas.Shared.Contracts;
using Atlas.Shared.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Modules.DeploymentIntelligence.Infrastructure;

public sealed class OutboxRelayOptions
{
    /// <summary>How often the relay looks for pending rows.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Rows claimed per pass. Bounded so a backlog drains steadily instead of in one huge burst.</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>Failed attempts before a row is abandoned (kept for inspection, never retried automatically).</summary>
    public int MaxAttempts { get; set; } = 10;

    /// <summary>How long a claimed row is invisible to other relay instances.</summary>
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan InitialBackoff { get; set; } = TimeSpan.FromSeconds(5);

    public TimeSpan MaxBackoff { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Publishes what the transactional outbox recorded. Runs as a hosted service
/// with cancellation support, never throws out of the loop, and leaves a row in
/// the table when the broker is unreachable — the whole point of the pattern.
///
/// Claiming works without a concurrency token: a conditional
/// <c>UPDATE … WHERE SentAtUtc IS NULL AND AbandonedAtUtc IS NULL AND NextAttemptAtUtc &lt;= now</c>
/// either wins the row (and pushes <c>NextAttemptAtUtc</c> into the future) or
/// reports zero rows affected, in which case another instance owns it. If the
/// process dies mid-publish the row becomes claimable again after the lease
/// expires, which is why delivery is at-least-once rather than exactly-once.
/// </summary>
public sealed class OutboxRelayService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxRelayService> _logger;
    private readonly OutboxRelayOptions _options;

    public OutboxRelayService(IServiceScopeFactory scopeFactory, ILogger<OutboxRelayService> logger,
        IOptions<OutboxRelayOptions>? options = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options?.Value ?? new OutboxRelayOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using (var probe = _scopeFactory.CreateScope())
        {
            if (probe.ServiceProvider.GetService<IRawEventPublisher>() is null)
            {
                _logger.LogWarning(
                    "Outbox relay is idle: no IRawEventPublisher is registered (Kafka:BootstrapServers is empty). " +
                    "Recorded events stay pending in deploymentintelligence.\"OutboxMessages\" until a broker is configured.");
                return;
            }
        }

        _logger.LogInformation("Outbox relay started (poll every {PollInterval}, batch {BatchSize}, max {MaxAttempts} attempts).",
            _options.PollInterval, _options.BatchSize, _options.MaxAttempts);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var published = await RunOnceAsync(stoppingToken);
                if (published == 0) await Task.Delay(_options.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A relay must never take the host down with it — a database blip
                // just delays the next pass.
                _logger.LogError(ex, "Outbox relay pass failed; will retry after {PollInterval}.", _options.PollInterval);
                await SafeDelayAsync(stoppingToken);
            }
        }

        _logger.LogInformation("Outbox relay stopped.");
    }

    /// <summary>
    /// One relay pass: claim up to <see cref="OutboxRelayOptions.BatchSize"/>
    /// pending rows and publish them. Exposed so an integration test can drive
    /// the real logic against real PostgreSQL without waiting for a timer.
    /// </summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var publisher = scope.ServiceProvider.GetService<IRawEventPublisher>();
        if (publisher is null) return 0;

        var db = scope.ServiceProvider.GetRequiredService<DeploymentIntelligenceDbContext>();
        var now = DateTimeOffset.UtcNow;

        var candidates = await db.OutboxMessages
            .Where(m => m.SentAtUtc == null && m.AbandonedAtUtc == null && m.NextAttemptAtUtc <= now)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(_options.BatchSize)
            .Select(m => m.Id)
            .ToListAsync(ct);

        var published = 0;
        foreach (var id in candidates)
        {
            var leaseUntil = DateTimeOffset.UtcNow + _options.LeaseDuration;
            var claimed = await db.OutboxMessages
                .Where(m => m.Id == id && m.SentAtUtc == null && m.AbandonedAtUtc == null && m.NextAttemptAtUtc <= now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(m => m.NextAttemptAtUtc, leaseUntil), ct);
            if (claimed == 0) continue; // another relay instance owns this row

            var message = await db.OutboxMessages.SingleAsync(m => m.Id == id, ct);
            try
            {
                await publisher.PublishRawAsync(message.Topic, message.PayloadJson, message.EventType, message.Version,
                    message.EventId, message.CorrelationId, message.CausationId, message.Producer, message.OccurredAtUtc, ct);

                message.MarkSent(DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(ct);
                published++;

                AtlasMetrics.EventsPublished.Add(1,
                    new KeyValuePair<string, object?>("topic", message.Topic),
                    new KeyValuePair<string, object?>("event_type", message.EventType),
                    new KeyValuePair<string, object?>("path", "outbox"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var attemptNumber = message.Attempts + 1;
                message.RecordFailure(ex.Message, BackoffFor(attemptNumber), _options.MaxAttempts, DateTimeOffset.UtcNow);
                await db.SaveChangesAsync(CancellationToken.None);

                if (message.IsAbandoned)
                {
                    AtlasMetrics.OutboxMessagesAbandoned.Add(1, new KeyValuePair<string, object?>("topic", message.Topic));
                    _logger.LogError(ex,
                        "Outbox message {EventId} ({EventType}) abandoned after {Attempts} attempts; it stays in the table for inspection.",
                        message.EventId, message.EventType, message.Attempts);
                }
                else
                {
                    _logger.LogWarning(ex,
                        "Outbox message {EventId} ({EventType}) attempt {Attempts} failed; retrying in {Delay}.",
                        message.EventId, message.EventType, message.Attempts, BackoffFor(attemptNumber));
                }
            }
        }

        return published;
    }

    /// <summary>Exponential backoff, capped. Pure, so the schedule is unit-tested.</summary>
    public static TimeSpan BackoffFor(int attempt, TimeSpan? initial = null, TimeSpan? max = null)
    {
        var start = initial ?? TimeSpan.FromSeconds(5);
        var ceiling = max ?? TimeSpan.FromMinutes(10);
        var multiplier = Math.Pow(2, Math.Max(0, attempt - 1));
        var millis = Math.Min(start.TotalMilliseconds * multiplier, ceiling.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(millis);
    }

    private async Task SafeDelayAsync(CancellationToken ct)
    {
        try { await Task.Delay(_options.PollInterval, ct); }
        catch (OperationCanceledException) { /* shutting down */ }
    }
}
