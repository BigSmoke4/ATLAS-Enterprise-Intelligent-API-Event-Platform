using Atlas.Modules.Reliability.Domain;

namespace Atlas.Modules.Reliability.Application;

/// <summary>
/// Process-wide, thread-safe home for CircuitBreaker instances keyed by
/// downstream identity (e.g. a service name or route). This is what lets
/// the same breaker instance be consulted by every request through
/// CircuitBreakerMiddleware AND updated by outbound HttpClient calls —
/// without it each request would get its own breaker and never trip.
/// </summary>
public interface ICircuitBreakerRegistry
{
    CircuitBreaker GetOrCreate(string key, double failureThreshold = 0.5, int minimumRequestVolume = 10,
        TimeSpan? samplingDuration = null, TimeSpan? openDuration = null);

    IReadOnlyDictionary<string, CircuitState> SnapshotStates();
}
