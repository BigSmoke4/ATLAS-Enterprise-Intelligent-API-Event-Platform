using Atlas.Modules.PolicyEngine.Domain;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.PolicyEngine.Application;

public class PolicyEvaluator : IPolicyEvaluator
{
    private readonly ILogger<PolicyEvaluator>? _logger;

    public PolicyEvaluator(ILogger<PolicyEvaluator>? logger = null) => _logger = logger;

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
            catch (PolicyEvaluationException ex)
            {
                // A rule referencing a fact we don't have yet is treated as
                // "did not match" rather than crashing evaluation of every
                // other rule — but the skip is recorded, not silent: the caller
                // sees Matched == false and the evaluator logs which rule was
                // skipped and why (the platform's structured logs are the
                // Observability module's input).
                _logger?.LogWarning(ex, "Policy rule {PolicyRule} v{Version} skipped during evaluation: {Reason}",
                    rule.Name, rule.Version, ex.Message);
                matched = false;
            }
            results.Add(new PolicyEvaluationOutcome(rule, matched));
        }
        return results;
    }
}
