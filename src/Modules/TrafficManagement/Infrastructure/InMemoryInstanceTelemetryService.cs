using Atlas.Modules.TrafficManagement.Application;
using System.Collections.Concurrent;

namespace Atlas.Modules.TrafficManagement.Infrastructure;

/// <summary>
/// Process-resident telemetry store. Connection counts and short-window
/// latency are volatile gauges by nature; ATLAS runs as a single modular
/// monolith, so the process memory is the authoritative surface for the
/// routing decision that same process makes. Reports survive only until the
/// reporter sends fresher ones — after the consumer's staleness window the
/// route refuses to use them rather than trusting old numbers. Registered
/// as a singleton; thread-safe.
/// </summary>
public sealed class InMemoryInstanceTelemetryService : IInstanceTelemetryService
{
    private readonly ConcurrentDictionary<(Guid Org, Guid Service, Guid Instance), InstanceTelemetry> _reports = new();
    private readonly TimeProvider _clock;

    public InMemoryInstanceTelemetryService(TimeProvider clock) => _clock = clock;

    public void Report(Guid organizationId, Guid serviceId, Guid instanceId, int activeConnections, double avgLatencyMs)
    {
        if (activeConnections < 0) throw new ArgumentOutOfRangeException(nameof(activeConnections), "Active connection count cannot be negative.");
        if (double.IsNaN(avgLatencyMs) || double.IsInfinity(avgLatencyMs) || avgLatencyMs < 0) throw new ArgumentOutOfRangeException(nameof(avgLatencyMs), "Latency must be a finite, non-negative number of milliseconds.");

        _reports[(organizationId, serviceId, instanceId)] =
            new InstanceTelemetry(instanceId, activeConnections, avgLatencyMs, _clock.GetUtcNow());
    }

    public IReadOnlyDictionary<Guid, InstanceTelemetry> GetLatest(Guid organizationId, Guid serviceId)
        => _reports
            .Where(kv => kv.Key.Org == organizationId && kv.Key.Service == serviceId)
            .ToDictionary(kv => kv.Key.Instance, kv => kv.Value);
}
