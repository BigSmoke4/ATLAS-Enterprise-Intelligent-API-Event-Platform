using Atlas.Modules.PolicyEngine.Domain;

namespace Atlas.Modules.PolicyEngine.Application;

/// <summary>
/// HTTP contract for a policy rule. Conditions and the action are data-only
/// value objects (never executed code), so they cross the wire as-is; the
/// aggregate itself — persistence state included — does not.
/// </summary>
public sealed record PolicyRuleDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    bool IsActive,
    int Version,
    IReadOnlyList<PolicyCondition> Conditions,
    PolicyAction Action,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static PolicyRuleDto From(PolicyRule rule) => new(
        rule.Id,
        rule.OrganizationId,
        rule.Name,
        rule.IsActive,
        rule.Version,
        rule.Conditions.ToArray(),
        rule.Action,
        rule.CreatedAtUtc,
        rule.UpdatedAtUtc);
}
