using Atlas.Modules.Reliability.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class TokenBucketRateLimiterTests
{
    [Fact]
    public void Allows_request_when_bucket_has_capacity()
    {
        var limiter = new TokenBucketRateLimiter(capacityTokens: 10, refillTokensPerSecond: 1);
        var now = DateTimeOffset.UtcNow;

        var (allowed, newLevel) = limiter.TryConsume(currentLevel: 10, lastRefillUtc: now, nowUtc: now, cost: 1);

        Assert.True(allowed);
        Assert.Equal(9, newLevel);
    }

    [Fact]
    public void Denies_request_when_bucket_empty_and_not_enough_time_elapsed()
    {
        var limiter = new TokenBucketRateLimiter(capacityTokens: 10, refillTokensPerSecond: 1);
        var now = DateTimeOffset.UtcNow;

        var (allowed, newLevel) = limiter.TryConsume(currentLevel: 0, lastRefillUtc: now, nowUtc: now.AddMilliseconds(100), cost: 1);

        Assert.False(allowed);
        Assert.True(newLevel < 1);
    }

    [Fact]
    public void Refills_over_time_up_to_capacity()
    {
        var limiter = new TokenBucketRateLimiter(capacityTokens: 5, refillTokensPerSecond: 1);
        var start = DateTimeOffset.UtcNow;

        var (allowed, newLevel) = limiter.TryConsume(currentLevel: 0, lastRefillUtc: start, nowUtc: start.AddSeconds(100), cost: 1);

        Assert.True(allowed);
        Assert.Equal(4, newLevel); // capped at capacity (5) then minus cost (1)
    }
}
