using Atlas.Shared.Domain;

namespace Atlas.Modules.TrafficManagement.Domain;

public enum TrafficPolicyMode { Standard, Canary, BlueGreen }

public sealed class TrafficPolicyConfiguration : TenantEntity
{
    public Guid ServiceId { get; private set; }
    public RoutingStrategyType Strategy { get; private set; }
    public TrafficPolicyMode Mode { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<TrafficPolicyTarget> _targets = new();
    public IReadOnlyCollection<TrafficPolicyTarget> Targets => _targets.AsReadOnly();

    private TrafficPolicyConfiguration() { }

    public static TrafficPolicyConfiguration Create(Guid organizationId, Guid serviceId, RoutingStrategyType strategy, TrafficPolicyMode mode)
    {
        var configuration = new TrafficPolicyConfiguration { OrganizationId = organizationId, ServiceId = serviceId, Strategy = strategy, Mode = mode };
        configuration.Touch();
        return configuration;
    }

    public void ReplaceTargets(IEnumerable<(Guid InstanceId, int WeightPercent, int Priority)> targets)
    {
        var materialized = targets.ToList();
        if (materialized.Count == 0) throw new ArgumentException("At least one routing target is required.");
        if (materialized.Any(t => t.WeightPercent < 0 || t.Priority < 0)) throw new ArgumentException("Target weights and priorities cannot be negative.");
        if ((Strategy is RoutingStrategyType.Weighted or RoutingStrategyType.Canary or RoutingStrategyType.BlueGreen) && materialized.Sum(t => t.WeightPercent) != 100)
            throw new ArgumentException("Weighted, canary, and blue/green targets must total exactly 100 percent.");

        _targets.Clear();
        _targets.AddRange(materialized.Select(t => TrafficPolicyTarget.Create(OrganizationId, Id, t.InstanceId, t.WeightPercent, t.Priority)));
        Touch();
    }

    public void Deactivate() { IsActive = false; Touch(); }
}

public sealed class TrafficPolicyTarget : TenantEntity
{
    public Guid PolicyId { get; private set; }
    public Guid InstanceId { get; private set; }
    public int WeightPercent { get; private set; }
    public int Priority { get; private set; }

    private TrafficPolicyTarget() { }
    public static TrafficPolicyTarget Create(Guid organizationId, Guid policyId, Guid instanceId, int weightPercent, int priority)
        => new() { OrganizationId = organizationId, PolicyId = policyId, InstanceId = instanceId, WeightPercent = weightPercent, Priority = priority };
}
