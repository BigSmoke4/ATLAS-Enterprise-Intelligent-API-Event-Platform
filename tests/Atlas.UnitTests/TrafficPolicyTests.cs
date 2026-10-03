using Atlas.Modules.TrafficManagement.Domain;
using Xunit;

namespace Atlas.UnitTests;

public sealed class TrafficPolicyTests
{
    [Fact]
    public void Canary_targets_must_total_one_hundred_percent()
    {
        var policy = TrafficPolicyConfiguration.Create(Guid.NewGuid(), Guid.NewGuid(), RoutingStrategyType.Canary, TrafficPolicyMode.Canary);
        Assert.Throws<ArgumentException>(() => policy.ReplaceTargets(new[] { (Guid.NewGuid(), 90, 0) }));
    }

    [Fact]
    public void Priority_selection_chooses_the_lowest_priority_healthy_target()
    {
        var targets = new[]
        {
            new RouteTarget("secondary", 0, true, 0, 0, 20),
            new RouteTarget("primary", 0, true, 0, 0, 1)
        };
        Assert.Equal("primary", RoutingStrategies.Priority(targets).ServiceInstanceId);
    }
}
