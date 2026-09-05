namespace Atlas.Modules.Reliability.Domain;

/// <summary>
/// Pure algorithmic token-bucket limiter. The distributed variant
/// (Atlas.Modules.Reliability.Infrastructure.RedisTokenBucketRateLimiter,
/// still a TODO) wraps the same math around Atlas.Shared.Contracts.IRateLimitStore
/// using a Lua script for atomic read-modify-write across nodes.
/// </summary>
public class TokenBucketRateLimiter
{
    public double CapacityTokens { get; }
    public double RefillTokensPerSecond { get; }

    public TokenBucketRateLimiter(double capacityTokens, double refillTokensPerSecond)
    {
        if (capacityTokens <= 0) throw new ArgumentOutOfRangeException(nameof(capacityTokens));
        if (refillTokensPerSecond <= 0) throw new ArgumentOutOfRangeException(nameof(refillTokensPerSecond));
        CapacityTokens = capacityTokens;
        RefillTokensPerSecond = refillTokensPerSecond;
    }

    /// <summary>
    /// Given the bucket's level at lastRefillUtc, computes whether a request of
    /// `cost` tokens is allowed "now", and returns the new level to persist.
    /// Kept as a pure function so both the in-memory and Redis-backed stores
    /// share identical, independently unit-testable math.
    /// </summary>
    public (bool Allowed, double NewLevel) TryConsume(double currentLevel, DateTimeOffset lastRefillUtc, DateTimeOffset nowUtc, double cost = 1.0)
    {
        var elapsedSeconds = Math.Max(0, (nowUtc - lastRefillUtc).TotalSeconds);
        var refilled = Math.Min(CapacityTokens, currentLevel + elapsedSeconds * RefillTokensPerSecond);

        if (refilled >= cost)
        {
            return (true, refilled - cost);
        }
        return (false, refilled);
    }
}
