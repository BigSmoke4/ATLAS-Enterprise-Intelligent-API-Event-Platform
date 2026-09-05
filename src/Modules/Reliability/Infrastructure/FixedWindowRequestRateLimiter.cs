using Atlas.Modules.Reliability.Application;
using Atlas.Shared.Contracts;

namespace Atlas.Modules.Reliability.Infrastructure;

/// <summary>
/// Fixed-window strategy on top of IRateLimitStore.IncrementAsync (atomic
/// INCR+EXPIRE in Redis — see RedisRateLimitStore). Chosen as the default
/// live-pipeline limiter for simplicity/cost; TokenBucketRateLimiter and
/// SlidingWindowCounter (Domain/) remain available for callers that need
/// smoother or exact-boundary behavior instead.
/// </summary>
public class FixedWindowRequestRateLimiter : IRequestRateLimiter
{
    private readonly IRateLimitStore _store;
    public FixedWindowRequestRateLimiter(IRateLimitStore store) => _store = store;

    public async Task<RateLimitDecision> CheckAsync(string scopeKey, int limitPerWindow, TimeSpan window, CancellationToken ct = default)
    {
        var key = $"ratelimit:{scopeKey}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / (long)window.TotalSeconds}";
        var count = await _store.IncrementAsync(key, window, ct);
        return new RateLimitDecision(count <= limitPerWindow, limitPerWindow, window, count);
    }
}
