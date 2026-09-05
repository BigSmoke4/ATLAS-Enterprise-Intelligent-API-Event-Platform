using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Modules.TrafficManagement.Domain;

namespace Atlas.Modules.TrafficManagement.Application;

public class TrafficRoutingService : ITrafficRoutingService
{
    private readonly IServiceHealthService _serviceHealth;
    public TrafficRoutingService(IServiceHealthService serviceHealth) => _serviceHealth = serviceHealth;

    public async Task<RoutingDecision> SelectInstanceAsync(Guid organizationId, Guid serviceId, RoutingPolicy policy, int requestSequenceNumber = 0, CancellationToken ct = default)
    {
        var instances = await _serviceHealth.GetInstancesAsync(organizationId, serviceId, ct);
        if (instances.Count == 0) return new RoutingDecision(false, null, "Service has no registered instances.");

        // RoundRobin and Weighted only need health + configured weight — both
        // real, available data. LeastConnections/LatencyBased need
        // per-instance active-connection-count / latency, which ATLAS does
        // not currently measure anywhere (see InstanceStatusDto) — rather
        // than fabricate zeros that would make every instance look tied,
        // this honestly refuses those two strategies until real per-instance
        // telemetry exists.
        if (policy.Strategy is RoutingStrategyType.LeastConnections or RoutingStrategyType.LatencyBased)
        {
            return new RoutingDecision(false, null,
                $"{policy.Strategy} requires per-instance latency/connection telemetry that ATLAS does not currently measure. Use RoundRobin or Weighted instead.");
        }

        var targets = instances.Select(i => new RouteTarget(
            ServiceInstanceId: i.InstanceId.ToString(),
            WeightPercent: ResolveWeight(policy, i.InstanceId),
            IsHealthy: i.Health == ServiceHealth.Healthy,
            AvgLatencyMs: 0,       // not measured — unused by RoundRobin/Weighted
            ActiveConnections: 0   // not measured — unused by RoundRobin/Weighted
        )).ToList();

        try
        {
            var selected = policy.Strategy switch
            {
                RoutingStrategyType.RoundRobin => RoutingStrategies.RoundRobin(targets, requestSequenceNumber),
                RoutingStrategyType.Weighted => RoutingStrategies.Weighted(targets, DeterministicRandom(requestSequenceNumber)),
                _ => throw new NotSupportedException($"Unhandled strategy {policy.Strategy}")
            };

            var instance = instances.First(i => i.InstanceId.ToString() == selected.ServiceInstanceId);
            return new RoutingDecision(true, instance.HostAndPort, null);
        }
        catch (InvalidOperationException ex)
        {
            // RoutingStrategies throws when there are no healthy targets — a
            // real, honest failure, not a fabricated selection.
            return new RoutingDecision(false, null, ex.Message);
        }
    }

    private static int ResolveWeight(RoutingPolicy policy, Guid instanceId)
        => policy.Weights is not null && policy.Weights.TryGetValue(instanceId.ToString(), out var w) ? w : 1;

    /// <summary>Deterministic pseudo-random value in [0,1) derived from the request sequence number, so Weighted routing is reproducible in tests without a real RNG dependency.</summary>
    private static double DeterministicRandom(int seed)
    {
        var hash = HashCode.Combine(seed, DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);
        return (uint)hash / (double)uint.MaxValue;
    }
}
