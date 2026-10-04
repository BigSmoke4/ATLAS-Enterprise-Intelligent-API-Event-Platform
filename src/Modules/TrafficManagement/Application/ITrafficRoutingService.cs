using Atlas.Modules.TrafficManagement.Domain;

namespace Atlas.Modules.TrafficManagement.Application;

public record RoutingDecision(bool Success, string? SelectedInstance, string? Reason);

/// <summary>
/// Combines a configured RoutingPolicy with REAL current service/instance
/// health from ServiceRegistry (via IServiceHealthService — an Application
/// interface, never ServiceRegistry's DbContext directly), REPORTED
/// per-instance gauges (active connections / average latency via
/// IInstanceTelemetryService — only reports inside the staleness window
/// qualify an instance), and the pure RoutingStrategies algorithms to pick
/// a target. No hard-coded routing results, no fabricated telemetry.
/// </summary>
public interface ITrafficRoutingService
{
    Task<RoutingDecision> SelectInstanceAsync(Guid organizationId, Guid serviceId, RoutingPolicy policy, int requestSequenceNumber = 0, CancellationToken ct = default);
    Task<RoutingDecision> SelectConfiguredInstanceAsync(Guid organizationId, Guid serviceId, int requestSequenceNumber = 0, CancellationToken ct = default);
}
