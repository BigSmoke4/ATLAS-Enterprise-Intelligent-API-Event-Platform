using System.Text.Json;
using Atlas.Shared.Contracts;
using Atlas.Shared.Observability;
using StackExchange.Redis;

namespace Atlas.Modules.Reliability.Infrastructure;

/// <summary>
/// In-process counter set for cache statistics. Registered as a singleton and
/// injected into both the cache implementation and the metrics endpoint, which
/// keeps the numbers per-container and testable (no static mutable state).
/// </summary>
public sealed class CacheStatistics : ICacheStatistics
{
    private long _hits;
    private long _misses;
    private long _sets;
    private long _invalidations;
    private long _failures;

    public void RecordHit() => Interlocked.Increment(ref _hits);
    public void RecordMiss() => Interlocked.Increment(ref _misses);
    public void RecordSet() => Interlocked.Increment(ref _sets);
    public void RecordInvalidation() => Interlocked.Increment(ref _invalidations);
    public void RecordFailure() => Interlocked.Increment(ref _failures);

    public CacheStatisticsSnapshot Snapshot() => new(
        Interlocked.Read(ref _hits),
        Interlocked.Read(ref _misses),
        Interlocked.Read(ref _sets),
        Interlocked.Read(ref _invalidations),
        Interlocked.Read(ref _failures));
}

/// <summary>
/// Redis-backed implementation of <see cref="ICacheService"/>.
///
/// Caching policy (documented in docs/architecture.md): only read-heavy,
/// non-sensitive configuration data is cached — route policy snapshots and
/// service metadata. Authorization decisions and user-specific transactional
/// state are never cached. Writes invalidate their own keys explicitly, and a
/// TTL bounds staleness for anything that slips through.
///
/// Failure policy: a cache outage degrades to the source of truth (callers see
/// a miss), never to a failed request.
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IConnectionMultiplexer _redis;
    private readonly ICacheStatistics _statistics;

    public RedisCacheService(IConnectionMultiplexer redis, ICacheStatistics statistics)
    {
        _redis = redis;
        _statistics = statistics;
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var value = await _redis.GetDatabase().StringGetAsync(key);
            if (!value.HasValue || value.IsNull)
            {
                _statistics.RecordMiss();
                AtlasMetrics.CacheMisses.Add(1);
                return default;
            }

            _statistics.RecordHit();
            AtlasMetrics.CacheHits.Add(1);
            return JsonSerializer.Deserialize<T>(value.ToString(), SerializerOptions);
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            // Cache is an optimization: never let it fail the caller.
            _statistics.RecordFailure();
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default)
    {
        if (ttl <= TimeSpan.Zero) return;

        try
        {
            var payload = JsonSerializer.Serialize(value, SerializerOptions);
            await _redis.GetDatabase().StringSetAsync(key, payload, ttl);
            _statistics.RecordSet();
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            _statistics.RecordFailure();
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(key);
            _statistics.RecordInvalidation();
        }
        catch (Exception ex) when (IsCacheFailure(ex))
        {
            _statistics.RecordFailure();
        }
    }

    private static bool IsCacheFailure(Exception ex)
        => ex is RedisException or TimeoutException or JsonException or NotSupportedException;
}
