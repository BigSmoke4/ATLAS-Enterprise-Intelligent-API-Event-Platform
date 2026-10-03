using Atlas.Modules.PolicyEngine.Domain;
using Xunit;

namespace Atlas.UnitTests;

public sealed class PolicyVersionTests
{
    [Fact]
    public void Creating_a_version_deactivates_the_previous_rule_and_increments_version()
    {
        var organizationId = Guid.NewGuid();
        var rule = PolicyRule.Create(organizationId, "High errors", new[] { new PolicyCondition("error_rate", ComparisonOperator.GreaterThan, 0.05) }, new PolicyAction(PolicyActionType.RaiseAlert));
        var next = rule.CreateNewVersion(new[] { new PolicyCondition("error_rate", ComparisonOperator.GreaterThan, 0.10) }, new PolicyAction(PolicyActionType.RaiseAlert, AlertSeverity: "Sev1"));

        Assert.False(rule.IsActive);
        Assert.True(next.IsActive);
        Assert.Equal(2, next.Version);
        Assert.Equal(rule.Name, next.Name);
    }
}
