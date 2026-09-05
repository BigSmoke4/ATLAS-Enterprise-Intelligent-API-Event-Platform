using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Modules.TrafficManagement.Application;
using Atlas.Modules.TrafficManagement.Domain;
using Xunit;

namespace Atlas.UnitTests;

file class FakeServiceHealthService : IServiceHealthService
{
    private readonly List<InstanceStatusDto> _instances;
    public FakeServiceHealthService(List<InstanceStatusDto> instances) => _instances = instances;

    public Task<Atlas.Shared.Application.Result<Guid>> RegisterServiceAsync(Guid o, Guid e, string n, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Atlas.Shared.Application.Result<Guid>> RegisterInstanceAsync(Guid o, Guid s, string h, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<Atlas.Shared.Application.Result> RecordHealthCheckAsync(Guid o, Guid i, bool s, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<ServiceStatusDto>> GetStatusAsync(Guid o, int page = 1, int pageSize = 50, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<InstanceStatusDto>> GetInstancesAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<InstanceStatusDto>>(_instances);
}

public class TrafficRoutingServiceTests
{
    private static InstanceStatusDto Healthy(string host) => new(Guid.NewGuid(), host, ServiceHealth.Healthy);
    private static InstanceStatusDto Unhealthy(string host) => new(Guid.NewGuid(), host, ServiceHealth.Unavailable);

    [Fact]
    public async Task Fails_cleanly_when_service_has_no_instances()
    {
        var service = new TrafficRoutingService(new FakeServiceHealthService(new List<InstanceStatusDto>()));
        var decision = await service.SelectInstanceAsync(Guid.NewGuid(), Guid.NewGuid(), new RoutingPolicy(RoutingStrategyType.RoundRobin));
        Assert.False(decision.Success);
    }

    [Fact]
    public async Task RoundRobin_selects_a_healthy_instance()
    {
        var instances = new List<InstanceStatusDto> { Healthy("10.0.0.1:80"), Unhealthy("10.0.0.2:80") };
        var service = new TrafficRoutingService(new FakeServiceHealthService(instances));

        var decision = await service.SelectInstanceAsync(Guid.NewGuid(), Guid.NewGuid(), new RoutingPolicy(RoutingStrategyType.RoundRobin), requestSequenceNumber: 3);

        Assert.True(decision.Success);
        Assert.Equal("10.0.0.1:80", decision.SelectedInstance);
    }

    [Fact]
    public async Task Fails_when_no_healthy_instances_exist()
    {
        var instances = new List<InstanceStatusDto> { Unhealthy("10.0.0.1:80"), Unhealthy("10.0.0.2:80") };
        var service = new TrafficRoutingService(new FakeServiceHealthService(instances));

        var decision = await service.SelectInstanceAsync(Guid.NewGuid(), Guid.NewGuid(), new RoutingPolicy(RoutingStrategyType.RoundRobin));

        Assert.False(decision.Success);
    }

    [Theory]
    [InlineData(RoutingStrategyType.LeastConnections)]
    [InlineData(RoutingStrategyType.LatencyBased)]
    public async Task Honestly_refuses_strategies_needing_unmeasured_telemetry(RoutingStrategyType strategy)
    {
        var instances = new List<InstanceStatusDto> { Healthy("10.0.0.1:80") };
        var service = new TrafficRoutingService(new FakeServiceHealthService(instances));

        var decision = await service.SelectInstanceAsync(Guid.NewGuid(), Guid.NewGuid(), new RoutingPolicy(strategy));

        Assert.False(decision.Success);
        Assert.Contains("does not currently measure", decision.Reason);
    }

    [Fact]
    public async Task Weighted_only_selects_among_healthy_instances()
    {
        var healthy = Healthy("10.0.0.1:80");
        var unhealthy = Unhealthy("10.0.0.2:80");
        var instances = new List<InstanceStatusDto> { healthy, unhealthy };
        var service = new TrafficRoutingService(new FakeServiceHealthService(instances));

        for (int i = 0; i < 20; i++)
        {
            var decision = await service.SelectInstanceAsync(Guid.NewGuid(), Guid.NewGuid(), new RoutingPolicy(RoutingStrategyType.Weighted), requestSequenceNumber: i);
            Assert.True(decision.Success);
            Assert.Equal("10.0.0.1:80", decision.SelectedInstance);
        }
    }
}
