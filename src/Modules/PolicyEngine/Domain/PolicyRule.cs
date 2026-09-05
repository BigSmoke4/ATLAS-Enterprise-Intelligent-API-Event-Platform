using Atlas.Shared.Domain;

namespace Atlas.Modules.PolicyEngine.Domain;

public enum PolicyActionType { ActivateCircuitBreaker, RecommendRollback, RaiseAlert }

public record PolicyAction(PolicyActionType Type, string? TargetServiceField = null);

/// <summary>
/// A rule is: ALL of its Conditions must be true (AND) for its Action to
/// fire. Only field/operator/value data is stored — never a script or
/// expression string that would need to be executed as code.
/// </summary>
public class PolicyRule : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;
    public int Version { get; private set; } = 1;

    private readonly List<PolicyCondition> _conditions = new();
    public IReadOnlyCollection<PolicyCondition> Conditions => _conditions.AsReadOnly();

    public PolicyAction Action { get; private set; } = null!;

    private PolicyRule() { }

    /// <summary>Returns true (and the rule should fire) only if every condition evaluates true against the given facts.</summary>
    public bool Matches(IReadOnlyDictionary<string, double> facts) => _conditions.All(c => c.Evaluate(facts));

    public static PolicyRule Create(Guid organizationId, string name, IEnumerable<PolicyCondition> conditions, PolicyAction action)
        => CreateInternal(organizationId, name, conditions, action, version: 1);

    private static PolicyRule CreateInternal(Guid organizationId, string name, IEnumerable<PolicyCondition> conditions, PolicyAction action, int version)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Policy name is required.", nameof(name));
        var conditionList = conditions.ToList();
        if (conditionList.Count == 0) throw new ArgumentException("A policy must have at least one condition.", nameof(conditions));

        var rule = new PolicyRule { OrganizationId = organizationId, Name = name, Action = action, Version = version };
        rule._conditions.AddRange(conditionList);
        return rule;
    }

    public void Deactivate() { IsActive = false; Touch(); }
    public void Activate() { IsActive = true; Touch(); }

    /// <summary>Creates a new version of this rule with updated conditions — old version is deactivated, not deleted, preserving audit history.</summary>
    public PolicyRule CreateNewVersion(IEnumerable<PolicyCondition> newConditions, PolicyAction newAction)
    {
        Deactivate();
        return CreateInternal(OrganizationId, Name, newConditions, newAction, version: Version + 1);
    }
}
