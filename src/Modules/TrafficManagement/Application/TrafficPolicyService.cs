using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.TrafficManagement.Domain;
using Atlas.Modules.TrafficManagement.Infrastructure;
using Atlas.Shared.Application;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.TrafficManagement.Application;

public sealed class TrafficPolicyService : ITrafficPolicyService
{
    private readonly TrafficManagementDbContext _db;
    private readonly IServiceHealthService _services;
    public TrafficPolicyService(TrafficManagementDbContext db, IServiceHealthService services) { _db = db; _services = services; }

    public async Task<Result<Guid>> UpsertAsync(Guid organizationId, Guid serviceId, RoutingStrategyType strategy, TrafficPolicyMode mode, IReadOnlyList<TrafficTargetInput> targets, CancellationToken ct = default)
    {
        var instances = await _services.GetInstancesAsync(organizationId, serviceId, ct);
        var validIds = instances.Select(i => i.InstanceId).ToHashSet();
        if (targets.Count == 0 || targets.Any(t => !validIds.Contains(t.InstanceId)))
            return Result.Failure<Guid>("Every routing target must be a registered instance of the service.", "VALIDATION_ERROR");

        var policy = await _db.Policies.Include(p => p.Targets).SingleOrDefaultAsync(p => p.OrganizationId == organizationId && p.ServiceId == serviceId, ct);
        try
        {
            if (policy is null)
            {
                policy = TrafficPolicyConfiguration.Create(organizationId, serviceId, strategy, mode);
                _db.Policies.Add(policy);
            }
            policy.ReplaceTargets(targets.Select(t => (t.InstanceId, t.WeightPercent, t.Priority)));
            await _db.SaveChangesAsync(ct);
            return Result.Success(policy.Id);
        }
        catch (ArgumentException ex) { return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR"); }
    }

    public async Task<TrafficPolicyDto?> GetAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default)
        => await _db.Policies.AsNoTracking().Where(p => p.OrganizationId == organizationId && p.ServiceId == serviceId)
            .Select(p => new TrafficPolicyDto(p.Id, p.ServiceId, p.Strategy, p.Mode, p.IsActive,
                p.Targets.Select(t => new TrafficTargetInput(t.InstanceId, t.WeightPercent, t.Priority)).ToList()))
            .SingleOrDefaultAsync(ct);
}
