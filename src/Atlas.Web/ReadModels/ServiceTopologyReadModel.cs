using Atlas.Modules.APIManagement.Application;
using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.Observability.Application;
using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Domain;

namespace Atlas.Web.ReadModels;

/// <summary>One node of the topology map — registry facts joined with real traffic telemetry.</summary>
public sealed record TopologyNode(
    Guid Id,
    string Name,
    string Health,
    int Instances,
    int HealthyInstances,
    int DegradedInstances,
    int UnhealthyInstances,
    double RequestsPerSecond,
    double? P95Ms,
    double? ErrorRate,
    string? DeploymentVersion,
    int RouteCount,
    bool HasTelemetry,
    IReadOnlyList<Guid> DependsOn,
    IReadOnlyList<TopologyInstance> InstanceDetails);

public sealed record TopologyInstance(Guid InstanceId, string HostAndPort, string Health);

public sealed record ServiceTopology(
    Guid OrganizationId,
    DateTimeOffset GeneratedAtUtc,
    bool HasTelemetry,
    string EvidenceSource,
    IReadOnlyList<TopologyNode> Nodes);

/// <summary>
/// Composes the topology read model from three application services
/// (ServiceRegistry, Observability, DeploymentIntelligence) plus the API
/// catalog. This lives in the composition root (Atlas.Web) on purpose: it is a
/// presentation-shaped join, not business logic, and putting it in any single
/// module would force that module to depend on three others.
///
/// Every field is a recorded fact. When telemetry is absent the node carries
/// <c>HasTelemetry = false</c> and the UI renders "no telemetry" instead of a
/// zero that looks like real traffic.
/// </summary>
public sealed class ServiceTopologyReadModel
{
    private const int MaxServicesPerRequest = 100;

    private readonly IServiceHealthService _services;
    private readonly ITelemetryQueryService _telemetry;
    private readonly IDeploymentRegressionService _deployments;
    private readonly IApiCatalogService _catalog;

    public ServiceTopologyReadModel(
        IServiceHealthService services,
        ITelemetryQueryService telemetry,
        IDeploymentRegressionService deployments,
        IApiCatalogService catalog)
    {
        _services = services;
        _telemetry = telemetry;
        _deployments = deployments;
        _catalog = catalog;
    }

    public async Task<ServiceTopology> BuildAsync(Guid organizationId, TimeSpan telemetryWindow, CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty)
        {
            return new ServiceTopology(organizationId, DateTimeOffset.UtcNow, false, "none", Array.Empty<TopologyNode>());
        }

        var statuses = (await _services.GetStatusAsync(organizationId, 1, MaxServicesPerRequest, ct)).ToList();
        var traffic = (await _telemetry.GetServiceSummariesAsync(organizationId, telemetryWindow, ct))
            .ToDictionary(summary => summary.ServiceId);
        var routes = await _catalog.ListAllRoutesAsync(organizationId, 1, 200, ct);
        var routeCounts = routes
            .Where(route => route.TargetServiceId.HasValue)
            .GroupBy(route => route.TargetServiceId!.Value)
            .ToDictionary(group => group.Key, group => group.Count());

        var nodes = new List<TopologyNode>(statuses.Count);
        var telemetryObserved = false;

        foreach (var status in statuses)
        {
            var instances = await _services.GetInstancesAsync(organizationId, status.ServiceId, ct);
            var dependencies = await _services.GetDependenciesAsync(organizationId, status.ServiceId, ct);
            var latestDeployment = await _deployments.ListAsync(organizationId, status.ServiceId, 1, 1, ct);

            traffic.TryGetValue(status.ServiceId, out var summary);
            if (summary is not null) telemetryObserved = true;

            nodes.Add(new TopologyNode(
                status.ServiceId,
                status.Name,
                status.AggregateHealth.ToString(),
                status.InstanceCount,
                status.HealthyInstanceCount,
                instances.Count(instance => instance.Health == ServiceHealth.Degraded),
                instances.Count(instance => instance.Health is ServiceHealth.Unhealthy or ServiceHealth.Unavailable),
                summary?.RequestsPerSecond ?? 0d,
                summary?.P95Ms,
                summary?.ErrorRate,
                latestDeployment.FirstOrDefault()?.Version,
                routeCounts.TryGetValue(status.ServiceId, out var routeCount) ? routeCount : 0,
                summary is not null,
                dependencies.Select(dependency => dependency.DependsOnServiceId).ToList(),
                instances.Select(instance => new TopologyInstance(instance.InstanceId, instance.HostAndPort, instance.Health.ToString())).ToList()));
        }

        return new ServiceTopology(
            organizationId,
            DateTimeOffset.UtcNow,
            telemetryObserved,
            telemetryObserved ? "service registry + live request telemetry (15m window)" : "service registry only (no request telemetry recorded)",
            nodes);
    }
}
