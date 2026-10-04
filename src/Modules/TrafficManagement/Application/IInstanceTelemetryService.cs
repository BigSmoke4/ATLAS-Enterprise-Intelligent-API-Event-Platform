namespace Atlas.Modules.TrafficManagement.Application;

/// <summary>
/// The latest reported gauge values for one service instance. Values are
/// exactly what the reporter supplied — ATLAS never invents them; consumers
/// decide how fresh a report must be to be trusted.
/// </summary>
public sealed record InstanceTelemetry(Guid InstanceId, int ActiveConnections, double AvgLatencyMs, DateTimeOffset RecordedAtUtc);

/// <summary>
/// Per-instance runtime telemetry (active connections, average latency)
/// reported by routers/gateways via POST /api/v1/traffic/telemetry and
/// consumed by the telemetry-driven routing strategies (LeastConnections,
/// LatencyBased). This is the real measurement source those strategies need:
/// routing decisions can now be based on actual observed instance state
/// instead of being refused for lack of data.
/// </summary>
public interface IInstanceTelemetryService
{
    /// <summary>Record the current gauge values for one instance. Replaces that instance's previous report.</summary>
    void Report(Guid organizationId, Guid serviceId, Guid instanceId, int activeConnections, double avgLatencyMs);

    /// <summary>Most recent report per instance for (organization, service). Staleness is the consumer's concern.</summary>
    IReadOnlyDictionary<Guid, InstanceTelemetry> GetLatest(Guid organizationId, Guid serviceId);
}
