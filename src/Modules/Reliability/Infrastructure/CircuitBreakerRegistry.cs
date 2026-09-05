using System.Collections.Concurrent;
using Atlas.Modules.Reliability.Application;
using Atlas.Modules.Reliability.Domain;

namespace Atlas.Modules.Reliability.Infrastructure;

public class CircuitBreakerRegistry : ICircuitBreakerRegistry
{
    private readonly ConcurrentDictionary<string, CircuitBreaker> _breakers = new();

    public CircuitBreaker GetOrCreate(string key, double failureThreshold = 0.5, int minimumRequestVolume = 10,
        TimeSpan? samplingDuration = null, TimeSpan? openDuration = null)
    {
        return _breakers.GetOrAdd(key, _ => new CircuitBreaker(key, failureThreshold, minimumRequestVolume, samplingDuration, openDuration));
    }

    public IReadOnlyDictionary<string, CircuitState> SnapshotStates()
        => _breakers.ToDictionary(kv => kv.Key, kv => kv.Value.State);
}
