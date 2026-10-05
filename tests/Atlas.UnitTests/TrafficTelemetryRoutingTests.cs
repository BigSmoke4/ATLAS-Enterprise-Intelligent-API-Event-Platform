using Atlas.Shared.Application;
using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Modules.TrafficManagement.Application;
using Atlas.Modules.TrafficManagement.Domain;
using Atlas.Modules.TrafficManagement.Infrastructure;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// End-to-end honesty checks for the telemetry-driven routing strategies:
/// reported per-instance gauges (active connections / average latency via
/// POST /api/v1/traffic/telemetry) decide LeastConnections/LatencyBased
/// routes; stale or missing reports exclude instances instead of being
/// replaced by fabricated values.
/// </summary>
public class TrafficTelemetryRoutingTests
{
    private static readonly Guid Org = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    private static readonly Guid Service = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");

    private static InstanceStatusDto Healthy(string host) => new(Guid.NewGuid(), host, ServiceHealth.Healthy);

    private sealed class FakeServiceHealthService : IServiceHealthService
    {
        private readonly List<InstanceStatusDto> _instances;
        public FakeServiceHealthService(List<InstanceStatusDto> instances) => _instances = instances;
        public Task<Atlas.Shared.Application.Result<Guid>> RegisterServiceAsync(Guid o, Guid e, string n, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Atlas.Shared.Application.Result<Guid>> RegisterInstanceAsync(Guid o, Guid s, string h, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Atlas.Shared.Application.Result> RecordHealthCheckAsync(Guid o, Guid i, bool s, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ServiceStatusDto>> GetStatusAsync(Guid o, int page = 1, int pageSize = 50, CancellationToken ct = default,
            string? sortBy = null, SortDirection sortDirection = SortDirection.Ascending) => throw new NotImplementedException();
        public Task<IReadOnlyList<InstanceStatusDto>> GetInstancesAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<InstanceStatusDto>>(_instances);
        public Task<Atlas.Shared.Application.Result> AddDependencyAsync(Guid o, Guid s, Guid d, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ServiceDependencyDto>> GetDependenciesAsync(Guid o, Guid s, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualTimeProvider(DateTimeOffset start) => _now = start;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private static (ManualTimeProvider Clock, InMemoryInstanceTelemetryService Telemetry, TrafficRoutingService Routing) Sut(
        List<InstanceStatusDto> instances)
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero));
        var telemetry = new InMemoryInstanceTelemetryService(clock);
        var routing = new TrafficRoutingService(new FakeServiceHealthService(instances),
            telemetry: telemetry, clock: clock, telemetryStaleness: TimeSpan.FromSeconds(60));
        return (clock, telemetry, routing);
    }

    [Fact]
    public async Task LeastConnections_routes_to_the_instance_with_the_fewest_reported_connections()
    {
        var busy = Healthy("10.0.0.1:80"); var idle = Healthy("10.0.0.2:80");
        var (_, telemetry, routing) = Sut(new List<InstanceStatusDto> { busy, idle });
        telemetry.Report(Org, Service, busy.InstanceId, activeConnections: 47, avgLatencyMs: 10);
        telemetry.Report(Org, Service, idle.InstanceId, activeConnections: 3, avgLatencyMs: 10);

        var decision = await routing.SelectInstanceAsync(Org, Service, new RoutingPolicy(RoutingStrategyType.LeastConnections));

        Assert.True(decision.Success);
        Assert.Equal("10.0.0.2:80", decision.SelectedInstance);
    }

    [Fact]
    public async Task LatencyBased_routes_to_the_instance_with_the_lowest_reported_latency()
    {
        var fast = Healthy("10.0.0.1:80"); var slow = Healthy("10.0.0.2:80");
        var (_, telemetry, routing) = Sut(new List<InstanceStatusDto> { fast, slow });
        telemetry.Report(Org, Service, fast.InstanceId, activeConnections: 100, avgLatencyMs: 8);
        telemetry.Report(Org, Service, slow.InstanceId, activeConnections: 1, avgLatencyMs: 240);

        var decision = await routing.SelectInstanceAsync(Org, Service, new RoutingPolicy(RoutingStrategyType.LatencyBased));

        Assert.True(decision.Success);
        Assert.Equal("10.0.0.1:80", decision.SelectedInstance);
    }

    [Fact]
    public async Task Instances_without_a_telemetry_report_are_excluded_from_telemetry_driven_strategies()
    {
        var reported = Healthy("10.0.0.1:80"); var silent = Healthy("10.0.0.2:80");
        var (_, telemetry, routing) = Sut(new List<InstanceStatusDto> { reported, silent });
        telemetry.Report(Org, Service, reported.InstanceId, activeConnections: 999, avgLatencyMs: 999);

        var decision = await routing.SelectInstanceAsync(Org, Service, new RoutingPolicy(RoutingStrategyType.LeastConnections));

        // "silent" never reported — routing must not invent a 0 for it even
        // though that would win; "reported" is the only eligible candidate.
        Assert.True(decision.Success);
        Assert.Equal("10.0.0.1:80", decision.SelectedInstance);
    }

    [Fact]
    public async Task Stale_reports_are_excluded_from_candidacy()
    {
        var stale = Healthy("10.0.0.1:80"); var fresh = Healthy("10.0.0.2:80");
        var (clock, telemetry, routing) = Sut(new List<InstanceStatusDto> { stale, fresh });
        telemetry.Report(Org, Service, stale.InstanceId, activeConnections: 1, avgLatencyMs: 5);
        clock.Advance(TimeSpan.FromSeconds(120)); // beyond the 60s staleness window
        telemetry.Report(Org, Service, fresh.InstanceId, activeConnections: 50, avgLatencyMs: 5);

        var decision = await routing.SelectInstanceAsync(Org, Service, new RoutingPolicy(RoutingStrategyType.LeastConnections));

        Assert.True(decision.Success);
        Assert.Equal("10.0.0.2:80", decision.SelectedInstance);
    }

    [Fact]
    public async Task Fails_honestly_when_no_instance_has_fresh_telemetry()
    {
        var (_, _, routing) = Sut(new List<InstanceStatusDto> { Healthy("10.0.0.1:80") });

        var decision = await routing.SelectInstanceAsync(Org, Service, new RoutingPolicy(RoutingStrategyType.LatencyBased));

        Assert.False(decision.Success);
        Assert.Contains("no instance", decision.Reason);
    }

    [Fact]
    public async Task Telemetry_driven_refusal_does_not_affect_health_based_strategies()
    {
        var instances = new List<InstanceStatusDto> { Healthy("10.0.0.1:80") };
        var (_, _, routing) = Sut(instances); // no reports at all

        var decision = await routing.SelectInstanceAsync(Org, Service, new RoutingPolicy(RoutingStrategyType.RoundRobin));

        Assert.True(decision.Success);
        Assert.Equal("10.0.0.1:80", decision.SelectedInstance);
    }

    [Fact]
    public void Report_replaces_the_previous_report_for_the_same_instance()
    {
        var (clock, telemetry, _) = Sut(new List<InstanceStatusDto>());
        var id = Guid.NewGuid();
        telemetry.Report(Org, Service, id, 5, 50);
        clock.Advance(TimeSpan.FromSeconds(10));
        telemetry.Report(Org, Service, id, 9, 120);

        var latest = telemetry.GetLatest(Org, Service)[id];
        Assert.Equal(9, latest.ActiveConnections);
        Assert.Equal(120, latest.AvgLatencyMs);
    }

    [Theory]
    [InlineData(-1, 10)]      // negative connection count
    [InlineData(0, -5)]       // negative latency
    [InlineData(0, double.NaN)]
    public void Report_rejects_impossible_gauge_values(int connections, double latency)
    {
        var (_, telemetry, _) = Sut(new List<InstanceStatusDto>());
        Assert.Throws<ArgumentOutOfRangeException>(() => telemetry.Report(Org, Service, Guid.NewGuid(), connections, latency));
    }

    [Fact]
    public void Telemetry_is_isolated_per_organization_and_service()
    {
        var (_, telemetry, _) = Sut(new List<InstanceStatusDto>());
        var id = Guid.NewGuid();
        telemetry.Report(Org, Service, id, 1, 1);

        Assert.Empty(telemetry.GetLatest(Guid.NewGuid(), Service));
        Assert.Empty(telemetry.GetLatest(Org, Guid.NewGuid()));
    }
}
