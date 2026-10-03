using Atlas.Modules.Reliability.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace Atlas.IntegrationTests;

public sealed class RedisRateLimitIntegrationTests
{
    [Fact]
    public async Task Redis_store_atomically_increments_a_shared_counter()
    {
        using var redis = await ConnectionMultiplexer.ConnectAsync(Environment.GetEnvironmentVariable("ConnectionStrings__Redis") ?? "localhost:6379");
        var store = new RedisRateLimitStore(redis);
        var key = $"integration:{Guid.NewGuid():N}";
        var tasks = Enumerable.Range(0, 20).Select(_ => store.IncrementAsync(key, TimeSpan.FromMinutes(1))).ToArray();
        var counts = await Task.WhenAll(tasks);

        Assert.Equal(20, counts.Max());
        Assert.Equal(20, counts.Distinct().Count());
        await redis.GetDatabase().KeyDeleteAsync(key);
    }
}
