using Atlas.Modules.ServiceRegistry.Domain;

namespace Atlas.Modules.ServiceRegistry.Application;

public interface IServiceHealthService
{
    Task<Result<Guid>> RegisterServiceAsync(Guid organizationId, Guid environmentId, string name, CancellationToken ct = default);
    Task<Result<Guid>> RegisterInstanceAsync(Guid organizationId, Guid serviceId, string hostAndPort, CancellationToken ct = default);
    Task<Result> RecordHealthCheckAsync(Guid organizationId, Guid instanceId, bool success, CancellationToken ct = default);
    Task<IReadOnlyList<ServiceStatusDto>> GetStatusAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default);

    /// <summary>
    /// Per-instance detail — the seam TrafficManagement uses to build real
    /// per-instance routing decisions instead of only aggregate health.
    /// ATLAS does not currently measure per-instance latency or active
    /// connection count anywhere, so this DTO deliberately does not include
    /// those fields — a caller needing them would be fabricating data if it
    /// invented numbers here.
    /// </summary>
    Task<IReadOnlyList<InstanceStatusDto>> GetInstancesAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default);
}

public record ServiceStatusDto(Guid ServiceId, string Name, ServiceHealth AggregateHealth, int InstanceCount, int HealthyInstanceCount);
public record InstanceStatusDto(Guid InstanceId, string HostAndPort, ServiceHealth Health);
