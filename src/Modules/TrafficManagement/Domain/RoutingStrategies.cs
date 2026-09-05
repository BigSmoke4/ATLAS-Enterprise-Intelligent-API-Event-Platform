namespace Atlas.Modules.TrafficManagement.Domain;

public record RouteTarget(string ServiceInstanceId, int WeightPercent, bool IsHealthy, double AvgLatencyMs, int ActiveConnections);

/// <summary>
/// Real routing algorithms operating on actual configured/observed service
/// state (weights from policy config, latency/connections from telemetry).
/// No result here is hard-coded — callers must supply real RouteTarget data
/// sourced from ServiceRegistry + Observability.
/// </summary>
public static class RoutingStrategies
{
    public static RouteTarget RoundRobin(IReadOnlyList<RouteTarget> targets, int requestIndex)
    {
        var healthy = HealthyOnly(targets);
        return healthy[requestIndex % healthy.Count];
    }

    public static RouteTarget Weighted(IReadOnlyList<RouteTarget> targets, double randomValue0To1)
    {
        var healthy = HealthyOnly(targets);
        var totalWeight = healthy.Sum(t => t.WeightPercent);
        if (totalWeight <= 0) throw new InvalidOperationException("No positive weight configured across healthy targets.");

        var threshold = randomValue0To1 * totalWeight;
        double cumulative = 0;
        foreach (var t in healthy)
        {
            cumulative += t.WeightPercent;
            if (threshold <= cumulative) return t;
        }
        return healthy[^1];
    }

    public static RouteTarget LeastConnections(IReadOnlyList<RouteTarget> targets)
        => HealthyOnly(targets).OrderBy(t => t.ActiveConnections).First();

    public static RouteTarget LatencyBased(IReadOnlyList<RouteTarget> targets)
        => HealthyOnly(targets).OrderBy(t => t.AvgLatencyMs).First();

    private static List<RouteTarget> HealthyOnly(IReadOnlyList<RouteTarget> targets)
    {
        var healthy = targets.Where(t => t.IsHealthy).ToList();
        if (healthy.Count == 0) throw new InvalidOperationException("No healthy targets available for routing.");
        return healthy;
    }
}
