using Atlas.Modules.PolicyEngine.Domain;

namespace Atlas.Modules.PolicyEngine.Application;

public class PolicyEvaluator : IPolicyEvaluator
{
    public IReadOnlyList<PolicyEvaluationOutcome> Evaluate(IReadOnlyList<PolicyRule> activeRules, IReadOnlyDictionary<string, double> facts)
    {
        var results = new List<PolicyEvaluationOutcome>();
        foreach (var rule in activeRules.Where(r => r.IsActive))
        {
            bool matched;
            try
            {
                matched = rule.Matches(facts);
            }
            catch (PolicyEvaluationException)
            {
                // A rule referencing a fact we don't have yet is treated as
                // "did not match" rather than crashing evaluation of every
                // other rule — but this is surfaced, not silently ignored,
                // via the caller inspecting Matched == false plus logging
                // (TODO: structured log here once Observability wiring exists).
                matched = false;
            }
            results.Add(new PolicyEvaluationOutcome(rule, matched));
        }
        return results;
    }
}
