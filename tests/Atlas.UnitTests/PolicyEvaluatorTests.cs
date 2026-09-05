using Atlas.Modules.PolicyEngine.Application;
using Atlas.Modules.PolicyEngine.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class PolicyEvaluatorTests
{
    [Fact]
    public void Rule_fires_when_all_conditions_are_true()
    {
        var rule = PolicyRule.Create(Guid.NewGuid(), "High error rate breaker",
            new[] { new PolicyCondition("error_rate", ComparisonOperator.GreaterThan, 0.05) },
            new PolicyAction(PolicyActionType.ActivateCircuitBreaker));

        var evaluator = new PolicyEvaluator();
        var outcomes = evaluator.Evaluate(new[] { rule }, new Dictionary<string, double> { ["error_rate"] = 0.10 });

        Assert.True(outcomes.Single().Matched);
    }

    [Fact]
    public void Rule_does_not_fire_when_a_condition_is_false()
    {
        var rule = PolicyRule.Create(Guid.NewGuid(), "High error rate breaker",
            new[] { new PolicyCondition("error_rate", ComparisonOperator.GreaterThan, 0.05) },
            new PolicyAction(PolicyActionType.ActivateCircuitBreaker));

        var evaluator = new PolicyEvaluator();
        var outcomes = evaluator.Evaluate(new[] { rule }, new Dictionary<string, double> { ["error_rate"] = 0.01 });

        Assert.False(outcomes.Single().Matched);
    }

    [Fact]
    public void All_conditions_must_match_not_just_one()
    {
        var rule = PolicyRule.Create(Guid.NewGuid(), "Canary rollback",
            new[]
            {
                new PolicyCondition("canary_error_rate", ComparisonOperator.GreaterThan, 0.10),
                new PolicyCondition("production_error_rate", ComparisonOperator.LessThan, 0.05),
            },
            new PolicyAction(PolicyActionType.RecommendRollback));

        var evaluator = new PolicyEvaluator();

        var facts1 = new Dictionary<string, double> { ["canary_error_rate"] = 0.15, ["production_error_rate"] = 0.10 };
        Assert.False(evaluator.Evaluate(new[] { rule }, facts1).Single().Matched);

        var facts2 = new Dictionary<string, double> { ["canary_error_rate"] = 0.15, ["production_error_rate"] = 0.02 };
        Assert.True(evaluator.Evaluate(new[] { rule }, facts2).Single().Matched);
    }

    [Fact]
    public void Deactivated_rules_are_excluded_from_evaluation()
    {
        var rule = PolicyRule.Create(Guid.NewGuid(), "Test rule",
            new[] { new PolicyCondition("x", ComparisonOperator.GreaterThan, 0) },
            new PolicyAction(PolicyActionType.RaiseAlert));
        rule.Deactivate();

        var evaluator = new PolicyEvaluator();
        var outcomes = evaluator.Evaluate(new[] { rule }, new Dictionary<string, double> { ["x"] = 1 });

        Assert.Empty(outcomes);
    }

    [Fact]
    public void Unknown_fact_is_treated_as_non_match_not_a_crash()
    {
        var rule = PolicyRule.Create(Guid.NewGuid(), "Test rule",
            new[] { new PolicyCondition("nonexistent_field", ComparisonOperator.GreaterThan, 0) },
            new PolicyAction(PolicyActionType.RaiseAlert));

        var evaluator = new PolicyEvaluator();
        var outcomes = evaluator.Evaluate(new[] { rule }, new Dictionary<string, double>());

        Assert.False(outcomes.Single().Matched);
    }

    [Fact]
    public void New_version_increments_version_and_deactivates_the_old_one()
    {
        var rule = PolicyRule.Create(Guid.NewGuid(), "Test rule",
            new[] { new PolicyCondition("x", ComparisonOperator.GreaterThan, 0) },
            new PolicyAction(PolicyActionType.RaiseAlert));

        var v2 = rule.CreateNewVersion(
            new[] { new PolicyCondition("x", ComparisonOperator.GreaterThan, 10) },
            new PolicyAction(PolicyActionType.RaiseAlert));

        Assert.False(rule.IsActive);
        Assert.True(v2.IsActive);
        Assert.Equal(2, v2.Version);
    }
}
