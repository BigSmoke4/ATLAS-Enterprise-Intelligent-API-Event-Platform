using Atlas.Modules.ServiceRegistry.Domain;
using Xunit;

namespace Atlas.UnitTests;

public sealed class ServiceDependencyTests
{
    [Fact]
    public void Dependency_rejects_self_reference()
    {
        var serviceId = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => ServiceDependency.Create(Guid.NewGuid(), serviceId, serviceId));
    }

    [Fact]
    public void Aggregate_health_is_derived_from_instances()
    {
        var service = RegisteredService.Create(Guid.NewGuid(), Guid.NewGuid(), "orders");
        service.RegisterInstance("orders-a:8080").RecordHealthCheck(true, DateTimeOffset.UtcNow);
        service.RegisterInstance("orders-b:8080").RecordHealthCheck(false, DateTimeOffset.UtcNow);

        Assert.Equal(ServiceHealth.Degraded, service.AggregateHealth());
    }
}
