using Atlas.Shared.Domain;

namespace Atlas.Modules.ServiceRegistry.Domain;

/// <summary>Normalized dependency edge. Keeping this as an entity makes topology queries and future extraction explicit.</summary>
public sealed class ServiceDependency : TenantEntity
{
    public Guid ServiceId { get; private set; }
    public Guid DependsOnServiceId { get; private set; }

    private ServiceDependency() { }

    public static ServiceDependency Create(Guid organizationId, Guid serviceId, Guid dependsOnServiceId)
    {
        if (serviceId == dependsOnServiceId) throw new ArgumentException("A service cannot depend on itself.");
        return new ServiceDependency { OrganizationId = organizationId, ServiceId = serviceId, DependsOnServiceId = dependsOnServiceId };
    }
}
