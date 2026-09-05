using Atlas.Modules.TrafficManagement.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class RoutingStrategiesTests
{
    private static List<RouteTarget> ThreeTargets() => new()
    {
        new RouteTarget("a", 70, true, AvgLatencyMs: 50, ActiveConnections: 10),
        new RouteTarget("b", 20, true, AvgLatencyMs: 20, ActiveConnections: 2),
        new RouteTarget("c", 10, false, AvgLatencyMs: 5, ActiveConnections: 0),
    };

    [Fact]
    public void RoundRobin_skips_unhealthy_targets()
    {
        var targets = ThreeTargets();
        var chosen = RoutingStrategies.RoundRobin(targets, requestIndex: 5);
        Assert.NotEqual("c", chosen.ServiceInstanceId);
    }

    [Fact]
    public void Weighted_never_selects_unhealthy_target()
    {
        var targets = ThreeTargets();
        for (double r = 0.0; r <= 1.0; r += 0.05)
        {
            var chosen = RoutingStrategies.Weighted(targets, r);
            Assert.NotEqual("c", chosen.ServiceInstanceId);
        }
    }

    [Fact]
    public void LeastConnections_picks_lowest_active_connections_among_healthy()
    {
        var targets = ThreeTargets();
        var chosen = RoutingStrategies.LeastConnections(targets);
        Assert.Equal("b", chosen.ServiceInstanceId); // c has 0 but is unhealthy
    }

    [Fact]
    public void LatencyBased_picks_lowest_latency_among_healthy()
    {
        var targets = ThreeTargets();
        var chosen = RoutingStrategies.LatencyBased(targets);
        Assert.Equal("b", chosen.ServiceInstanceId); // c has lower latency but is unhealthy
    }

    [Fact]
    public void Throws_when_no_healthy_targets_available()
    {
        var targets = new List<RouteTarget> { new("a", 100, false, 10, 0) };
        Assert.Throws<InvalidOperationException>(() => RoutingStrategies.LeastConnections(targets));
    }
}
