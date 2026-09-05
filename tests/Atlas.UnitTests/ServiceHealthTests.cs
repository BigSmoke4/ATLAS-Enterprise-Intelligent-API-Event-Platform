using Atlas.Modules.ServiceRegistry.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class ServiceHealthTests
{
    [Fact]
    public void No_instances_means_unavailable()
    {
        var service = RegisteredService.Create(Guid.NewGuid(), Guid.NewGuid(), "payments");
        Assert.Equal(ServiceHealth.Unavailable, service.AggregateHealth());
    }

    [Fact]
    public void All_instances_healthy_means_service_healthy()
    {
        var service = RegisteredService.Create(Guid.NewGuid(), Guid.NewGuid(), "payments");
        var i1 = service.RegisterInstance("10.0.0.1:8080");
        var i2 = service.RegisterInstance("10.0.0.2:8080");
        i1.RecordHealthCheck(true, DateTimeOffset.UtcNow);
        i2.RecordHealthCheck(true, DateTimeOffset.UtcNow);

        Assert.Equal(ServiceHealth.Healthy, service.AggregateHealth());
    }

    [Fact]
    public void Mixed_instance_health_means_service_degraded()
    {
        var service = RegisteredService.Create(Guid.NewGuid(), Guid.NewGuid(), "payments");
        var i1 = service.RegisterInstance("10.0.0.1:8080");
        var i2 = service.RegisterInstance("10.0.0.2:8080");
        i1.RecordHealthCheck(true, DateTimeOffset.UtcNow);
        i2.RecordHealthCheck(false, DateTimeOffset.UtcNow);

        Assert.Equal(ServiceHealth.Degraded, service.AggregateHealth());
    }

    [Fact]
    public void All_instances_unavailable_means_service_unavailable()
    {
        var service = RegisteredService.Create(Guid.NewGuid(), Guid.NewGuid(), "payments");
        var i1 = service.RegisterInstance("10.0.0.1:8080");
        for (int i = 0; i < 5; i++) i1.RecordHealthCheck(false, DateTimeOffset.UtcNow);

        Assert.Equal(ServiceHealth.Unavailable, i1.Health);
        Assert.Equal(ServiceHealth.Unavailable, service.AggregateHealth());
    }

    [Fact]
    public void Consecutive_failures_escalate_from_degraded_to_unhealthy_to_unavailable()
    {
        var service = RegisteredService.Create(Guid.NewGuid(), Guid.NewGuid(), "orders");
        var instance = service.RegisterInstance("10.0.0.1:8080");

        instance.RecordHealthCheck(false, DateTimeOffset.UtcNow);
        Assert.Equal(ServiceHealth.Degraded, instance.Health);

        instance.RecordHealthCheck(false, DateTimeOffset.UtcNow);
        Assert.Equal(ServiceHealth.Unhealthy, instance.Health);

        instance.RecordHealthCheck(false, DateTimeOffset.UtcNow);
        instance.RecordHealthCheck(false, DateTimeOffset.UtcNow);
        Assert.Equal(ServiceHealth.Unavailable, instance.Health);

        instance.RecordHealthCheck(true, DateTimeOffset.UtcNow);
        Assert.Equal(ServiceHealth.Healthy, instance.Health);
    }
}
