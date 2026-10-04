using System.Threading.Channels;
using Atlas.Modules.Observability.Application;
using Atlas.Modules.Observability.Domain;
using Atlas.Shared.Contracts;
using Atlas.Shared.Observability;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Atlas.Modules.Observability.Infrastructure;

public sealed class TelemetryAggregationOptions
{
    /// <summary>How often buffered observations are merged into PostgreSQL.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Telemetry retention. Buckets older than this are deleted by a periodic cleanup.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(14);

    /// <summary>Upper bound on distinct open buckets held in memory if the database is unavailable.</summary>
    public int MaxPendingKeys { get; set; } = 20_000;
}

/// <summary>
/// Background service that turns the fire-and-forget ingest buffer into
/// durable one-minute aggregates. Long-running work never happens inside an
/// HTTP request: the request path only writes to an in-memory channel.
///
/// Failure policy: if PostgreSQL is unavailable the batch is re-queued and
/// retried on the next tick (bounded by <see cref="TelemetryAggregationOptions.MaxPendingKeys"/>),
/// with a counter and a structured log line — the platform keeps serving
/// traffic and telemetry resumes without operator intervention.
/// </summary>
public sealed class TelemetryAggregationService : BackgroundService
{
    private readonly TelemetryIngestBuffer _buffer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelemetryAggregationService> _logger;
    private readonly TelemetryAggregationOptions _options;
    private readonly IActiveDeploymentVersionProvider? _deployments;

    private readonly Dictionary<PendingKey, PendingBucket> _pending = new();
    private readonly object _gate = new();
    private long _rejectedKeys;

    public TelemetryAggregationService(
        TelemetryIngestBuffer buffer,
        IServiceScopeFactory scopeFactory,
        IOptions<TelemetryAggregationOptions> options,
        ILogger<TelemetryAggregationService> logger,
        IActiveDeploymentVersionProvider? deployments = null)
    {
        _buffer = buffer;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _deployments = deployments;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telemetry aggregation started (flush every {FlushInterval}, retention {Retention}, deployment attribution: {Attribution}).",
            _options.FlushInterval, _options.Retention, _deployments is null ? "not available" : "enabled");

        var flushDeadline = DateTimeOffset.UtcNow + _options.FlushInterval;
        var cleanupDeadline = DateTimeOffset.UtcNow + TimeSpan.FromHours(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                readCts.CancelAfter(TimeSpan.FromMilliseconds(500));
                var observation = await _buffer.ReadAsync(readCts.Token);
                Accumulate(observation);
                _buffer.MarkProcessed(1);
            }
            catch (OperationCanceledException)
            {
                if (stoppingToken.IsCancellationRequested) break;
                // read timeout — fall through to the flush check
            }
            catch (ChannelClosedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Telemetry buffer read failed; continuing.");
            }

            if (DateTimeOffset.UtcNow >= flushDeadline)
            {
                await FlushAsync(stoppingToken);
                flushDeadline = DateTimeOffset.UtcNow + _options.FlushInterval;
            }

            if (DateTimeOffset.UtcNow >= cleanupDeadline)
            {
                await ApplyRetentionAsync(stoppingToken);
                cleanupDeadline = DateTimeOffset.UtcNow + TimeSpan.FromHours(1);
            }
        }

        // Graceful shutdown: persist whatever is still buffered.
        await FlushAsync(CancellationToken.None);
    }

    private void Accumulate(in RequestTelemetryObservation observation)
    {
        var key = new PendingKey(observation.OrganizationId, observation.ServiceId, observation.Route,
            observation.HttpMethod.ToUpperInvariant(), RequestTelemetryAggregate.TruncateToMinute(observation.ObservedAtUtc));

        lock (_gate)
        {
            if (!_pending.TryGetValue(key, out var bucket))
            {
                if (_pending.Count >= _options.MaxPendingKeys)
                {
                    Interlocked.Increment(ref _rejectedKeys);
                    AtlasMetrics.TelemetrySamplesDropped.Add(1);
                    return;
                }
                bucket = new PendingBucket();
                _pending[key] = bucket;
            }
            bucket.Apply(observation.StatusCode, observation.DurationMs);
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        Dictionary<PendingKey, PendingBucket> batch;
        lock (_gate)
        {
            if (_pending.Count == 0) return;
            batch = new Dictionary<PendingKey, PendingBucket>(_pending);
            _pending.Clear();
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ObservabilityDbContext>();

            var from = batch.Keys.Min(k => k.WindowStartUtc);
            var to = batch.Keys.Max(k => k.WindowStartUtc);
            var organizationIds = batch.Keys.Select(k => k.OrganizationId).Distinct().ToList();

            // One round trip for the whole flush window; the aggregation scope
            // is a background service with no tenant context, so the global
            // query filter is inactive and this intentionally spans tenants.
            var existing = await db.RequestTelemetryAggregates
                .Where(a => organizationIds.Contains(a.OrganizationId) && a.WindowStartUtc >= from && a.WindowStartUtc <= to)
                .ToListAsync(ct);

            var versionCache = new Dictionary<(Guid OrganizationId, Guid ServiceId), string>();
            var persisted = 0;

            foreach (var (key, bucket) in batch)
            {
                var version = await ResolveDeploymentVersionAsync(key, versionCache, ct);

                var row = existing.FirstOrDefault(a =>
                    a.OrganizationId == key.OrganizationId &&
                    a.ServiceId == key.ServiceId &&
                    a.Route == key.Route &&
                    a.HttpMethod == key.HttpMethod &&
                    a.WindowStartUtc == key.WindowStartUtc &&
                    a.DeploymentVersion == version);

                if (row is null)
                {
                    row = RequestTelemetryAggregate.Create(key.OrganizationId, key.WindowStartUtc, key.ServiceId, key.Route, key.HttpMethod, version);
                    row.Merge(bucket.Requests, bucket.ServerErrors, bucket.ClientErrors, bucket.TotalDurationMs, bucket.MaxDurationMs, bucket.Buckets);
                    db.RequestTelemetryAggregates.Add(row);
                    existing.Add(row);
                }
                else
                {
                    row.Merge(bucket.Requests, bucket.ServerErrors, bucket.ClientErrors, bucket.TotalDurationMs, bucket.MaxDurationMs, bucket.Buckets);
                }

                persisted++;
            }

            await db.SaveChangesAsync(ct);
            AtlasMetrics.TelemetryBucketsPersisted.Add(persisted);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            Requeue(batch);
        }
        catch (Exception ex)
        {
            AtlasMetrics.TelemetryFlushFailures.Add(1);
            _logger.LogWarning(ex, "Telemetry flush failed; {Count} bucket(s) retained in memory for retry.", batch.Count);
            Requeue(batch);
        }
    }

    private void Requeue(Dictionary<PendingKey, PendingBucket> batch)
    {
        lock (_gate)
        {
            foreach (var (key, bucket) in batch)
            {
                if (_pending.TryGetValue(key, out var existing))
                {
                    existing.Add(bucket);
                }
                else if (_pending.Count < _options.MaxPendingKeys)
                {
                    _pending[key] = bucket;
                }
                else
                {
                    Interlocked.Increment(ref _rejectedKeys);
                    AtlasMetrics.TelemetrySamplesDropped.Add(1);
                }
            }
        }
    }

    private async Task<string> ResolveDeploymentVersionAsync(PendingKey key, Dictionary<(Guid, Guid), string> cache, CancellationToken ct)
    {
        if (_deployments is null || !key.ServiceId.HasValue) return string.Empty;

        var cacheKey = (key.OrganizationId, key.ServiceId.Value);
        if (cache.TryGetValue(cacheKey, out var cached)) return cached;

        string version;
        try
        {
            version = await _deployments.GetActiveVersionAsync(key.OrganizationId, key.ServiceId.Value, key.WindowStartUtc, ct) ?? string.Empty;
        }
        catch (Exception ex)
        {
            // Deployment attribution is an enrichment, not a correctness
            // requirement: an unattributed bucket is recorded honestly rather
            // than being given an invented version.
            _logger.LogDebug(ex, "Deployment version lookup failed; telemetry bucket stays unattributed.");
            version = string.Empty;
        }

        cache[cacheKey] = version;
        return version;
    }

    private async Task ApplyRetentionAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ObservabilityDbContext>();
            var cutoff = DateTimeOffset.UtcNow - _options.Retention;
            var removed = await db.RequestTelemetryAggregates.Where(a => a.WindowStartUtc < cutoff).ExecuteDeleteAsync(ct);
            if (removed > 0)
            {
                _logger.LogInformation("Telemetry retention removed {Removed} bucket(s) older than {Cutoff:o}.", removed, cutoff);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Telemetry retention cleanup failed; will retry next cycle.");
        }
    }

    /// <summary>Rejected/re-queued key count — surfaced for diagnostics, deliberately not a metric ATLAS fakes away.</summary>
    public long RejectedKeys => Interlocked.Read(ref _rejectedKeys);

    private readonly record struct PendingKey(Guid OrganizationId, Guid? ServiceId, string Route, string HttpMethod, DateTimeOffset WindowStartUtc);

    private sealed class PendingBucket
    {
        public long Requests { get; private set; }
        public long ServerErrors { get; private set; }
        public long ClientErrors { get; private set; }
        public double TotalDurationMs { get; private set; }
        public double MaxDurationMs { get; private set; }
        public long[] Buckets { get; } = LatencyHistogram.CreateEmptyBucketCounts();

        public void Apply(int statusCode, double durationMs)
        {
            if (durationMs < 0) durationMs = 0;
            Requests++;
            if (statusCode >= 500) ServerErrors++;
            else if (statusCode >= 400) ClientErrors++;
            TotalDurationMs += durationMs;
            if (durationMs > MaxDurationMs) MaxDurationMs = durationMs;
            Buckets[LatencyHistogram.BucketIndex(durationMs)]++;
        }

        public void Add(PendingBucket other)
        {
            Requests += other.Requests;
            ServerErrors += other.ServerErrors;
            ClientErrors += other.ClientErrors;
            TotalDurationMs += other.TotalDurationMs;
            if (other.MaxDurationMs > MaxDurationMs) MaxDurationMs = other.MaxDurationMs;
            LatencyHistogram.Add(other.Buckets, Buckets);
        }
    }
}
