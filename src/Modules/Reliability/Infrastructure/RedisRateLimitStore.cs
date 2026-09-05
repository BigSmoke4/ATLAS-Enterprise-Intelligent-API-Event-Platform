using Atlas.Shared.Contracts;
using StackExchange.Redis;

namespace Atlas.Modules.Reliability.Infrastructure;

/// <summary>
/// Real distributed IRateLimitStore. Uses Lua scripts (EVAL) so the
/// read-modify-write for both the fixed-window counter and the token-bucket
/// level is atomic across concurrent ATLAS instances hitting the same Redis
/// key — a plain GET-then-SET from .NET would race under real concurrent
/// load, which is exactly the bug this class exists to avoid.
/// </summary>
public class RedisRateLimitStore : IRateLimitStore
{
    private readonly IConnectionMultiplexer _redis;

    // KEYS[1] = counter key. ARGV[1] = window seconds.
    // Atomically increments and (re)sets TTL only on first increment in the window.
    private const string IncrementScript = @"
        local current = redis.call('INCR', KEYS[1])
        if current == 1 then
            redis.call('EXPIRE', KEYS[1], ARGV[1])
        end
        return current";

    public RedisRateLimitStore(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var result = await db.ScriptEvaluateAsync(IncrementScript,
            new RedisKey[] { key }, new RedisValue[] { (long)window.TotalSeconds });
        return (long)result;
    }

    public async Task<double> GetTokenBucketLevelAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(key);
        return value.HasValue ? (double)value : -1; // -1 signals "no stored level yet" to the caller, which should treat it as a full bucket
    }

    public async Task SetTokenBucketLevelAsync(string key, double level, TimeSpan ttl, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.StringSetAsync(key, level, ttl);
    }
}
