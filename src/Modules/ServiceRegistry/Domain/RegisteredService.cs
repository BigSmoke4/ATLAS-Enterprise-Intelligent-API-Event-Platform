using Atlas.Shared.Domain;

namespace Atlas.Modules.ServiceRegistry.Domain;

public class RegisteredService : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public Guid EnvironmentId { get; private set; }

    private readonly List<ServiceInstance> _instances = new();
    public IReadOnlyCollection<ServiceInstance> Instances => _instances.AsReadOnly();

    private readonly List<Guid> _dependsOnServiceIds = new();
    public IReadOnlyCollection<Guid> DependsOnServiceIds => _dependsOnServiceIds.AsReadOnly();

    private RegisteredService() { }

    public static RegisteredService Create(Guid organizationId, Guid environmentId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Service name is required.", nameof(name));
        return new RegisteredService { OrganizationId = organizationId, EnvironmentId = environmentId, Name = name };
    }

    public ServiceInstance RegisterInstance(string hostAndPort)
    {
        var instance = ServiceInstance.Create(OrganizationId, Id, hostAndPort);
        _instances.Add(instance);
        Touch();
        return instance;
    }

    public void AddDependency(Guid dependsOnServiceId)
    {
        if (dependsOnServiceId == Id) throw new InvalidOperationException("A service cannot depend on itself.");
        if (!_dependsOnServiceIds.Contains(dependsOnServiceId)) _dependsOnServiceIds.Add(dependsOnServiceId);
    }

    /// <summary>
    /// Aggregate health is derived from instance health, never set directly —
    /// this is the rule the master prompt's "no fake routing/health" clause
    /// is about: the service's health is a computed fact, not a flag someone
    /// flips in an admin UI.
    /// </summary>
    public ServiceHealth AggregateHealth()
    {
        if (_instances.Count == 0) return ServiceHealth.Unavailable;

        var healthyCount = _instances.Count(i => i.Health == ServiceHealth.Healthy);
        var unavailableCount = _instances.Count(i => i.Health == ServiceHealth.Unavailable);

        if (unavailableCount == _instances.Count) return ServiceHealth.Unavailable;
        if (healthyCount == _instances.Count) return ServiceHealth.Healthy;
        if (healthyCount == 0) return ServiceHealth.Unhealthy;
        return ServiceHealth.Degraded;
    }
}
