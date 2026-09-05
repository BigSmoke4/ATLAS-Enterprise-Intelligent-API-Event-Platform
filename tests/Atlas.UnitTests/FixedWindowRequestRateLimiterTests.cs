using Atlas.Modules.Reliability.Infrastructure;
using Atlas.Shared.Contracts;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>In-memory stand-in for Redis so the limiter's decision logic is verifiable without a live server.</summary>
file class FakeRateLimitStore : IRateLimitStore
{
    private readonly Dictionary<string, long> _counters = new();
    public Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        _counters[key] = _counters.GetValueOrDefault(key) + 1;
        return Task.FromResult(_counters[key]);
    }
    public Task<double> GetTokenBucketLevelAsync(string key, CancellationToken ct = default) => Task.FromResult(-1.0);
    public Task SetTokenBucketLevelAsync(string key, double level, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;
}

public class FixedWindowRequestRateLimiterTests
{
    [Fact]
    public async Task Allows_requests_under_the_limit()
    {
        var limiter = new FixedWindowRequestRateLimiter(new FakeRateLimitStore());

        for (int i = 0; i < 5; i++)
        {
            var decision = await limiter.CheckAsync("ip:1.2.3.4", limitPerWindow: 10, window: TimeSpan.FromMinutes(1));
            Assert.True(decision.Allowed);
        }
    }

    [Fact]
    public async Task Denies_requests_once_limit_is_exceeded()
    {
        var limiter = new FixedWindowRequestRateLimiter(new FakeRateLimitStore());

        RateLimitDecision? last = null;
        for (int i = 0; i < 12; i++)
        {
            last = await limiter.CheckAsync("ip:5.6.7.8", limitPerWindow: 10, window: TimeSpan.FromMinutes(1));
        }

        Assert.False(last!.Allowed);
        Assert.Equal(12, last.CurrentCount);
    }

    [Fact]
    public async Task Different_scope_keys_are_independent()
    {
        var limiter = new FixedWindowRequestRateLimiter(new FakeRateLimitStore());

        for (int i = 0; i < 10; i++) await limiter.CheckAsync("ip:a", 10, TimeSpan.FromMinutes(1));
        var decisionB = await limiter.CheckAsync("ip:b", 10, TimeSpan.FromMinutes(1));

        Assert.True(decisionB.Allowed);
        Assert.Equal(1, decisionB.CurrentCount);
    }
}
