using Atlas.Modules.TrafficManagement.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.TrafficManagement.Application;

public interface ITrafficPolicyService
{
    Task<Result<Guid>> UpsertAsync(Guid organizationId, Guid serviceId, RoutingStrategyType strategy, TrafficPolicyMode mode, IReadOnlyList<TrafficTargetInput> targets, CancellationToken ct = default);
    Task<TrafficPolicyDto?> GetAsync(Guid organizationId, Guid serviceId, CancellationToken ct = default);
}

public sealed record TrafficTargetInput(Guid InstanceId, int WeightPercent, int Priority);
public sealed record TrafficPolicyDto(Guid Id, Guid ServiceId, RoutingStrategyType Strategy, TrafficPolicyMode Mode, bool IsActive, IReadOnlyList<TrafficTargetInput> Targets);
