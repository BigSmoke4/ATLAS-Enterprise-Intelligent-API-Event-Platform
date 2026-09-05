namespace Atlas.Shared.Contracts;

/// <summary>Redis-backed cache abstraction. Swappable for an in-memory fake in unit tests.</summary>
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default);
    Task RemoveAsync(string key, CancellationToken ct = default);
}

/// <summary>Distributed lock abstraction (Redlock-style) for coordination across app instances.</summary>
public interface IDistributedLock
{
    /// <returns>An IAsyncDisposable that releases the lock, or null if the lock could not be acquired.</returns>
    Task<IAsyncDisposable?> AcquireAsync(string resource, TimeSpan expiry, CancellationToken ct = default);
}

/// <summary>
/// Backing store for distributed rate limiting counters. Must remain correct
/// when multiple ATLAS instances evaluate the same limiter key concurrently.
/// </summary>
public interface IRateLimitStore
{
    /// <returns>The current count in the window/bucket after this increment is applied.</returns>
    Task<long> IncrementAsync(string key, TimeSpan window, CancellationToken ct = default);
    Task<double> GetTokenBucketLevelAsync(string key, CancellationToken ct = default);
    Task SetTokenBucketLevelAsync(string key, double level, TimeSpan ttl, CancellationToken ct = default);
}
