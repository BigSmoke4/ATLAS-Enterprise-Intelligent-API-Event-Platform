using Atlas.Shared.Domain;

namespace Atlas.Modules.ServiceRegistry.Domain;

public class ServiceInstance : TenantEntity
{
    public Guid ServiceId { get; private set; }
    public string HostAndPort { get; private set; } = string.Empty;
    public ServiceHealth Health { get; private set; } = ServiceHealth.Unavailable;
    public DateTimeOffset? LastHealthCheckUtc { get; private set; }
    public int ConsecutiveFailedChecks { get; private set; }

    private ServiceInstance() { }

    public static ServiceInstance Create(Guid organizationId, Guid serviceId, string hostAndPort)
    {
        if (string.IsNullOrWhiteSpace(hostAndPort)) throw new ArgumentException("Host/port is required.", nameof(hostAndPort));
        return new ServiceInstance { OrganizationId = organizationId, ServiceId = serviceId, HostAndPort = hostAndPort };
    }

    /// <summary>
    /// Records the outcome of an actual health-check probe. Health is
    /// derived from real check history, not assigned arbitrarily:
    /// 2+ consecutive failures => Unhealthy, 4+ => Unavailable, any single
    /// recent failure after a healthy streak => Degraded.
    /// </summary>
    public void RecordHealthCheck(bool success, DateTimeOffset atUtc)
    {
        LastHealthCheckUtc = atUtc;

        if (success)
        {
            ConsecutiveFailedChecks = 0;
            Health = ServiceHealth.Healthy;
            return;
        }

        ConsecutiveFailedChecks++;
        Health = ConsecutiveFailedChecks switch
        {
            1 => ServiceHealth.Degraded,
            2 or 3 => ServiceHealth.Unhealthy,
            _ => ServiceHealth.Unavailable
        };
    }
}
