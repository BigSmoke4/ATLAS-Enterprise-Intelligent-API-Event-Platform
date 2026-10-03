using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Modules.ServiceRegistry.Infrastructure;
using Atlas.Shared.Application;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.ServiceRegistry.Application;

public class ServiceHealthService : IServiceHealthService
{
    private readonly ServiceRegistryDbContext _db;
    public ServiceHealthService(ServiceRegistryDbContext db) => _db = db;

    public async Task<Result<Guid>> RegisterServiceAsync(Guid organizationId, Guid environmentId, string name, CancellationToken ct = default)
    {
        var exists = await _db.Services.AnyAsync(s => s.OrganizationId == organizationId && s.EnvironmentId == environmentId && s.Name == name, ct);
        if (exists) return Result.Failure<Guid>($"Service '{name}' already registered in this environment.", "DUPLICATE_SERVICE");

        try
        {
            var service = RegisteredService.Create(organizationId, environmentId, name.Trim());
            _db.Services.Add(service);
            await _db.SaveChangesAsync(ct);
            return Result.Success(service.Id);
        }
        catch (ArgumentException ex) { return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR"); }
    }

    public async Task<Result<Guid>> RegisterInstanceAsync(Guid organizationId, Guid serviceId, string hostAndPort, CancellationToken ct = default)
    {
        var service = await _db.Services.Include(s => s.Instances)
            .FirstOrDefaultAsync(s => s.Id == serviceId && s.OrganizationId == organizationId, ct);
        if (service is null) return Result.Failure<Guid>("Service not found.", "NOT_FOUND");

        try
        {
            var instance = service.RegisterInstance(hostAndPort);
            await _db.SaveChangesAsync(ct);
            return Result.Success(instance.Id);
        }
        catch (ArgumentException ex) { return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR"); }
        catch (InvalidOperationException ex) { return Result.Failure<Guid>(ex.Message, "CONFLICT"); }
    }

    public async Task<Result> RecordHealthCheckAsync(Guid organizationId, Guid instanceId, bool success, CancellationToken ct = default)
    {
        var instance = await _db.Instances.FirstOrDefaultAsync(i => i.Id == instanceId && i.OrganizationId == organizationId, ct);
        if (instance is null) return Result.Failure("Service instance not found.", "NOT_FOUND");

        instance.RecordHealthCheck(success, DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }


    public async Task<IReadOnlyList<InstanceStatusDto>> GetInstancesAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default)
    {
        return await _db.Instances.AsNoTracking()
            .Where(i => i.OrganizationId == organizationId && i.ServiceId == serviceId)
            .Select(i => new InstanceStatusDto(i.Id, i.HostAndPort, i.Health))
            .ToListAsync(ct);
    }

    public async Task<Result> AddDependencyAsync(Guid organizationId, Guid serviceId, Guid dependsOnServiceId, CancellationToken ct = default)
    {
        if (serviceId == dependsOnServiceId) return Result.Failure("A service cannot depend on itself.", "VALIDATION_ERROR");
        var services = await _db.Services.Where(s => s.OrganizationId == organizationId && (s.Id == serviceId || s.Id == dependsOnServiceId)).Select(s => s.Id).ToListAsync(ct);
        if (services.Count != 2) return Result.Failure("Both services must exist in the same organization.", "NOT_FOUND");
        if (await _db.Dependencies.AnyAsync(d => d.OrganizationId == organizationId && d.ServiceId == serviceId && d.DependsOnServiceId == dependsOnServiceId, ct))
            return Result.Failure("That dependency already exists.", "CONFLICT");
        _db.Dependencies.Add(ServiceDependency.Create(organizationId, serviceId, dependsOnServiceId));
        await _db.SaveChangesAsync(ct);
        return Result.Success();
    }

    public async Task<IReadOnlyList<ServiceDependencyDto>> GetDependenciesAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default)
        => await _db.Dependencies.AsNoTracking()
            .Where(d => d.OrganizationId == organizationId && d.ServiceId == serviceId)
            .Select(d => new ServiceDependencyDto(d.ServiceId, d.DependsOnServiceId))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ServiceStatusDto>> GetStatusAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var services = await _db.Services.Include(s => s.Instances)
            .Where(s => s.OrganizationId == organizationId)
            .OrderBy(s => s.Name)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsNoTracking()
            .ToListAsync(ct);

        return services.Select(s => new ServiceStatusDto(
            s.Id, s.Name, s.AggregateHealth(), s.Instances.Count,
            s.Instances.Count(i => i.Health == ServiceHealth.Healthy))).ToList();
    }
}
