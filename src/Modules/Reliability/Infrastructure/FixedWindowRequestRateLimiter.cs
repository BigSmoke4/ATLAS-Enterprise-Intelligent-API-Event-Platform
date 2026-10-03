using Atlas.Modules.Reliability.Application;
using Atlas.Shared.Contracts;

namespace Atlas.Modules.Reliability.Infrastructure;

public class FixedWindowRequestRateLimiter : IRequestRateLimiter
{
    private readonly IRateLimitStore _store;
    public FixedWindowRequestRateLimiter(IRateLimitStore store) => _store = store;

    public async Task<RateLimitDecision> CheckAsync(string scopeKey, int limitPerWindow, TimeSpan window, CancellationToken ct = default, RateLimitAlgorithm algorithm = RateLimitAlgorithm.FixedWindow)
    {
        if (limitPerWindow <= 0) throw new ArgumentOutOfRangeException(nameof(limitPerWindow));
        if (window <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(window));
        var key = $"ratelimit:{algorithm}:{scopeKey}";
        return algorithm switch
        {
            RateLimitAlgorithm.FixedWindow => Fixed(key, limitPerWindow, window, await _store.IncrementAsync($"{key}:{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / Math.Max(1, (long)window.TotalSeconds)}", window, ct)),
            RateLimitAlgorithm.SlidingWindow => Sliding(key, limitPerWindow, window, await _store.IncrementSlidingWindowAsync(key, limitPerWindow, window, ct)),
            RateLimitAlgorithm.TokenBucket => Token(key, limitPerWindow, window, await _store.TryConsumeTokenBucketAsync(key, limitPerWindow, limitPerWindow / window.TotalSeconds, 1, window, ct)),
            RateLimitAlgorithm.LeakyBucket => Leaky(key, limitPerWindow, window, await _store.TryConsumeLeakyBucketAsync(key, limitPerWindow, limitPerWindow / window.TotalSeconds, window, ct)),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
    }

    private static RateLimitDecision Fixed(string key, int limit, TimeSpan window, long count) => new(count <= limit, limit, window, count);
    private static RateLimitDecision Sliding(string key, int limit, TimeSpan window, (bool Allowed, long CurrentCount) result) => new(result.Allowed, limit, window, result.CurrentCount);
    private static RateLimitDecision Leaky(string key, int limit, TimeSpan window, (bool Allowed, long CurrentCount) result) => new(result.Allowed, limit, window, result.CurrentCount);
    private static RateLimitDecision Token(string key, int limit, TimeSpan window, (bool Allowed, double Level) result) => new(result.Allowed, limit, window, Math.Max(0, limit - (long)Math.Floor(result.Level)));
}
