using Atlas.Modules.Reliability.Application;
using Atlas.Modules.Reliability.Infrastructure;
using Xunit;

namespace Atlas.UnitTests;

public sealed class DistributedRateLimitAlgorithmTests
{
    [Theory]
    [InlineData(RateLimitAlgorithm.FixedWindow)]
    [InlineData(RateLimitAlgorithm.TokenBucket)]
    [InlineData(RateLimitAlgorithm.SlidingWindow)]
    [InlineData(RateLimitAlgorithm.LeakyBucket)]
    public async Task Every_configured_algorithm_enforces_a_limit(RateLimitAlgorithm algorithm)
    {
        var limiter = new FixedWindowRequestRateLimiter(new TestStore());
        var first = await limiter.CheckAsync("tenant:a", 2, TimeSpan.FromMinutes(1), algorithm: algorithm);
        var second = await limiter.CheckAsync("tenant:a", 2, TimeSpan.FromMinutes(1), algorithm: algorithm);
        var third = await limiter.CheckAsync("tenant:a", 2, TimeSpan.FromMinutes(1), algorithm: algorithm);

        Assert.True(first.Allowed);
        Assert.True(second.Allowed);
        Assert.False(third.Allowed);
    }

    private sealed class TestStore : Atlas.Shared.Contracts.IRateLimitStore
    {
        private readonly Dictionary<string, long> _counts = new();
        public Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default) => Task.FromResult(Next(key));
        public Task<double> GetTokenBucketLevelAsync(string key, CancellationToken ct = default) => Task.FromResult(-1d);
        public Task SetTokenBucketLevelAsync(string key, double level, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;
        public Task<(bool Allowed, long CurrentCount)> IncrementSlidingWindowAsync(string key, int limit, TimeSpan window, CancellationToken ct = default)
        { var count = Next(key); return Task.FromResult((count <= limit, count)); }
        public Task<(bool Allowed, long CurrentCount)> TryConsumeLeakyBucketAsync(string key, int capacity, double leakPerSecond, TimeSpan ttl, CancellationToken ct = default)
        { var count = Next(key); return Task.FromResult((count <= capacity, count)); }
        public Task<(bool Allowed, double Level)> TryConsumeTokenBucketAsync(string key, double capacity, double refillPerSecond, double cost, TimeSpan ttl, CancellationToken ct = default)
        { var count = Next(key); return Task.FromResult((count <= capacity, capacity - count)); }
        private long Next(string key) { _counts[key] = _counts.GetValueOrDefault(key) + 1; return _counts[key]; }
    }
}
