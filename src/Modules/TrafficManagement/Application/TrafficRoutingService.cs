using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Modules.TrafficManagement.Domain;

namespace Atlas.Modules.TrafficManagement.Application;

public class TrafficRoutingService : ITrafficRoutingService
{
    private readonly IServiceHealthService _serviceHealth;
    private readonly ITrafficPolicyService? _policies;
    private readonly IInstanceTelemetryService? _telemetry;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _telemetryStaleness;

    /// <param name="telemetryStaleness">Maximum age of an instance telemetry report for the telemetry-driven strategies (LeastConnections/LatencyBased) to trust it. Default 60s.</param>
    public TrafficRoutingService(IServiceHealthService serviceHealth, ITrafficPolicyService? policies = null,
        IInstanceTelemetryService? telemetry = null, TimeProvider? clock = null, TimeSpan? telemetryStaleness = null)
    {
        _serviceHealth = serviceHealth;
        _policies = policies;
        _telemetry = telemetry;
        _clock = clock ?? TimeProvider.System;
        _telemetryStaleness = telemetryStaleness ?? TimeSpan.FromSeconds(60);
    }

    public async Task<RoutingDecision> SelectConfiguredInstanceAsync(Guid organizationId, Guid serviceId, int requestSequenceNumber = 0, CancellationToken ct = default)
    {
        if (_policies is null) return new RoutingDecision(false, null, "Traffic policy service is not configured.");
        var configured = await _policies.GetAsync(organizationId, serviceId, ct);
        if (configured is null) return new RoutingDecision(false, null, "No active traffic policy is configured for this service.");
        var weights = configured.Targets.ToDictionary(t => t.InstanceId.ToString(), t => t.WeightPercent);
        var priorities = configured.Targets.ToDictionary(t => t.InstanceId.ToString(), t => t.Priority);
        return await SelectInstanceAsync(organizationId, serviceId, new RoutingPolicy(configured.Strategy, weights, priorities), requestSequenceNumber, ct);
    }

    public async Task<RoutingDecision> SelectInstanceAsync(Guid organizationId, Guid serviceId, RoutingPolicy policy, int requestSequenceNumber = 0, CancellationToken ct = default)
    {
        var instances = await _serviceHealth.GetInstancesAsync(organizationId, serviceId, ct);
        if (instances.Count == 0) return new RoutingDecision(false, null, "Service has no registered instances.");

        // Telemetry-driven strategies (LeastConnections, LatencyBased) route on
        // REPORTED per-instance gauges: active connections and average latency
        // pushed by routers/gateways via POST /api/v1/traffic/telemetry, kept by
        // IInstanceTelemetryService. Honesty rule: only instances with a report
        // fresher than the staleness window are eligible; an instance with no
        // (or stale) telemetry is excluded rather than fed fabricated zeros.
        IReadOnlyDictionary<Guid, InstanceTelemetry>? telemetryByInstance = null;
        if (policy.Strategy is RoutingStrategyType.LeastConnections or RoutingStrategyType.LatencyBased)
        {
            if (_telemetry is null)
                return new RoutingDecision(false, null, $"{policy.Strategy} requires per-instance telemetry reporting (POST /api/v1/traffic/telemetry), which is not wired in this host.");

            telemetryByInstance = _telemetry.GetLatest(organizationId, serviceId);
        }

        var targets = instances
            .Select(i => BuildTarget(policy, i, telemetryByInstance))
            // For telemetry-driven strategies, drop instances without fresh telemetry.
            .Where(t => t.FreshTelemetry)
            .Select(t => t.Target)
            .ToList();

        if (telemetryByInstance is not null && targets.Count == 0)
        {
            return new RoutingDecision(false, null,
                $"{policy.Strategy} needs per-instance telemetry reported within the last {_telemetryStaleness.TotalSeconds:0}s (POST /api/v1/traffic/telemetry); no instance of this service has a fresh report.");
        }

        try
        {
            var selected = policy.Strategy switch
            {
                RoutingStrategyType.RoundRobin => RoutingStrategies.RoundRobin(targets, requestSequenceNumber),
                RoutingStrategyType.Weighted or RoutingStrategyType.Canary or RoutingStrategyType.BlueGreen => RoutingStrategies.Weighted(targets, DeterministicRandom(requestSequenceNumber)),
                RoutingStrategyType.Priority => RoutingStrategies.Priority(targets),
                RoutingStrategyType.LeastConnections => RoutingStrategies.LeastConnections(targets),
                RoutingStrategyType.LatencyBased => RoutingStrategies.LatencyBased(targets),
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

    /// <summary>
    /// Attach reported telemetry when the strategy requires it. FreshTelemetry is
    /// false only for telemetry-driven strategies whose instance lacks a report
    /// inside the staleness window — those are excluded from candidacy upstream.
    /// </summary>
    private (RouteTarget Target, bool FreshTelemetry) BuildTarget(RoutingPolicy policy, InstanceStatusDto i,
        IReadOnlyDictionary<Guid, InstanceTelemetry>? telemetryByInstance)
    {
        InstanceTelemetry? report = null;
        var fresh = true;
        if (telemetryByInstance is not null)
        {
            if (telemetryByInstance.TryGetValue(i.InstanceId, out var r) &&
                _clock.GetUtcNow() - r.RecordedAtUtc <= _telemetryStaleness)
            {
                report = r;
            }
            else
            {
                fresh = false;
            }
        }

        var target = new RouteTarget(
            ServiceInstanceId: i.InstanceId.ToString(),
            WeightPercent: ResolveWeight(policy, i.InstanceId),
            IsHealthy: i.Health == ServiceHealth.Healthy,
            AvgLatencyMs: report?.AvgLatencyMs ?? 0,
            ActiveConnections: report?.ActiveConnections ?? 0,
            Priority: policy.Priorities is not null && policy.Priorities.TryGetValue(i.InstanceId.ToString(), out var priority) ? priority : 0);
        return (target, fresh);
    }

    /// <summary>Deterministic pseudo-random value in [0,1) derived from the request sequence number, so Weighted routing is reproducible in tests without a real RNG dependency.</summary>
    private static double DeterministicRandom(int seed)
    {
        var hash = HashCode.Combine(seed, DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond);
        return (uint)hash / (double)uint.MaxValue;
    }
}
