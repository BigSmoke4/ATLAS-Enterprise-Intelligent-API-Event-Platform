using Atlas.Shared.Contracts;
using StackExchange.Redis;

namespace Atlas.Modules.Reliability.Infrastructure;

/// <summary>Atomic Redis implementations of all distributed rate-limit primitives.</summary>
public class RedisRateLimitStore : IRateLimitStore
{
    private readonly IConnectionMultiplexer _redis;
    public RedisRateLimitStore(IConnectionMultiplexer redis) => _redis = redis;

    private const string IncrementScript = @"
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end
        return current";

    private const string SlidingScript = @"
        local now = tonumber(ARGV[1])
        local cutoff = now - tonumber(ARGV[2])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, cutoff)
        redis.call('ZADD', KEYS[1], now, ARGV[3])
        local count = redis.call('ZCARD', KEYS[1])
        redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[2]) + 1000)
        return count";

    private const string LeakyScript = @"
        local now = tonumber(ARGV[1])
        local interval = 1000 / tonumber(ARGV[2])
        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, now)
        local count = redis.call('ZCARD', KEYS[1])
        local capacity = tonumber(ARGV[3])
        if count >= capacity then return {0, count} end
        local member = ARGV[4]
        redis.call('ZADD', KEYS[1], now + interval, member)
        redis.call('PEXPIRE', KEYS[1], tonumber(ARGV[5]))
        return {1, count + 1}";

    private const string TokenScript = @"
        local now = tonumber(ARGV[1])
        local capacity = tonumber(ARGV[2])
        local refill = tonumber(ARGV[3])
        local cost = tonumber(ARGV[4])
        local raw = redis.call('GET', KEYS[1])
        local level = capacity
        local last = now
        if raw then
            local separator = string.find(raw, '|')
            level = tonumber(string.sub(raw, 1, separator - 1))
            last = tonumber(string.sub(raw, separator + 1))
            level = math.min(capacity, level + math.max(0, now - last) / 1000 * refill)
        end
        local allowed = 0
        if level >= cost then level = level - cost; allowed = 1 end
        redis.call('SET', KEYS[1], tostring(level) .. '|' .. tostring(now), 'PX', ARGV[5])
        return {allowed, level}";

    public async Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        var result = await _redis.GetDatabase().ScriptEvaluateAsync(IncrementScript, [new RedisKey(key)], [(RedisValue)Math.Max(1, (long)window.TotalSeconds)]);
        return (long)result;
    }

    public async Task<double> GetTokenBucketLevelAsync(string key, CancellationToken ct = default)
    {
        var value = await _redis.GetDatabase().StringGetAsync(key);
        if (!value.HasValue) return -1;
        var separator = value.ToString().IndexOf('|');
        return separator < 0 ? double.Parse(value.ToString(), System.Globalization.CultureInfo.InvariantCulture) : double.Parse(value.ToString()[..separator], System.Globalization.CultureInfo.InvariantCulture);
    }

    public Task SetTokenBucketLevelAsync(string key, double level, TimeSpan ttl, CancellationToken ct = default)
        => _redis.GetDatabase().StringSetAsync(key, level.ToString(System.Globalization.CultureInfo.InvariantCulture), ttl);

    public async Task<(bool Allowed, long CurrentCount)> IncrementSlidingWindowAsync(string key, int limit, TimeSpan window, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = await _redis.GetDatabase().ScriptEvaluateAsync(SlidingScript, [new RedisKey(key)], [now, (long)window.TotalMilliseconds, $"{now}:{Guid.NewGuid():N}"]);
        var count = (long)result;
        return (count <= limit, count);
    }

    public async Task<(bool Allowed, long CurrentCount)> TryConsumeLeakyBucketAsync(string key, int capacity, double leakPerSecond, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = (RedisResult[])await _redis.GetDatabase().ScriptEvaluateAsync(LeakyScript, [new RedisKey(key)], [now, leakPerSecond, capacity, Guid.NewGuid().ToString("N"), (long)ttl.TotalMilliseconds]);
        return ((long)result[0] == 1, (long)result[1]);
    }

    public async Task<(bool Allowed, double Level)> TryConsumeTokenBucketAsync(string key, double capacity, double refillPerSecond, double cost, TimeSpan ttl, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var result = (RedisResult[])await _redis.GetDatabase().ScriptEvaluateAsync(TokenScript, [new RedisKey(key)], [now, capacity, refillPerSecond, cost, (long)ttl.TotalMilliseconds]);
        return ((long)result[0] == 1, (double)result[1]);
    }
}
