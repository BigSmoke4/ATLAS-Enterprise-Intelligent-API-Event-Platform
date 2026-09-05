using Atlas.Shared.Contracts;
using StackExchange.Redis;

namespace Atlas.Modules.Reliability.Infrastructure;

/// <summary>
/// Simple single-node Redis lock (SET NX PX + a token, released with a
/// compare-and-delete Lua script so a lock can't be released by a holder
/// that already expired and lost it to someone else). Sufficient for a
/// single-Redis-instance deployment; a true Redlock across multiple
/// independent Redis nodes is a documented future upgrade, not implemented
/// here — do not rely on this for correctness-critical multi-region locking.
/// </summary>
public class RedisDistributedLock : IDistributedLock
{
    private readonly IConnectionMultiplexer _redis;
    public RedisDistributedLock(IConnectionMultiplexer redis) => _redis = redis;

    private const string ReleaseScript = @"
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        else
            return 0
        end";

    public async Task<IAsyncDisposable?> AcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var token = Guid.NewGuid().ToString("N");
        var key = $"lock:{resource}";

        var acquired = await db.StringSetAsync(key, token, expiry, When.NotExists);
        if (!acquired) return null;

        return new Releaser(db, key, token);
    }

    private sealed class Releaser : IAsyncDisposable
    {
        private readonly IDatabase _db;
        private readonly string _key;
        private readonly string _token;
        public Releaser(IDatabase db, string key, string token) { _db = db; _key = key; _token = token; }

        public async ValueTask DisposeAsync()
        {
            await _db.ScriptEvaluateAsync(ReleaseScript, new RedisKey[] { _key }, new RedisValue[] { _token });
        }
    }
}
