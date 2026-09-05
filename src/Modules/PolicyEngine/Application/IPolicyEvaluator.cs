using Atlas.Modules.PolicyEngine.Domain;

namespace Atlas.Modules.PolicyEngine.Application;

public record PolicyEvaluationOutcome(PolicyRule Rule, bool Matched);

/// <summary>
/// Evaluates a set of active rules against a fact snapshot (e.g. current
/// error_rate, canary_error_rate, production_error_rate for a service) and
/// returns which fired. Deliberately read-only / advisory: this does NOT
/// itself flip a circuit breaker or trigger a rollback — see
/// docs/ai-architecture.md and the master prompt's requirement that
/// destructive actions require explicit authorization + confirmation.
/// </summary>
public interface IPolicyEvaluator
{
    IReadOnlyList<PolicyEvaluationOutcome> Evaluate(IReadOnlyList<PolicyRule> activeRules, IReadOnlyDictionary<string, double> facts);
}
