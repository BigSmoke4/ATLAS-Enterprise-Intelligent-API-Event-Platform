using Atlas.Modules.TrafficManagement.Domain;

namespace Atlas.Modules.TrafficManagement.Application;

public record RoutingDecision(bool Success, string? SelectedInstance, string? Reason);

/// <summary>
/// Combines a configured RoutingPolicy with REAL current service/instance
/// health from ServiceRegistry (via IServiceHealthService — an Application
/// interface, never ServiceRegistry's DbContext directly) and the pure
/// RoutingStrategies algorithms to pick a target. This is what the master
/// prompt means by "routing decisions must be based on actual configured
/// service state" — no hard-coded routing results.
/// </summary>
public interface ITrafficRoutingService
{
    Task<RoutingDecision> SelectInstanceAsync(Guid organizationId, Guid serviceId, RoutingPolicy policy, int requestSequenceNumber = 0, CancellationToken ct = default);
}
