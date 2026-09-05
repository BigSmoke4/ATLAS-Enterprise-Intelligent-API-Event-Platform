namespace Atlas.Modules.PolicyEngine.Domain;

public enum ComparisonOperator { GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Equal, NotEqual }

/// <summary>
/// A single "field OP value" comparison, e.g. error_rate &gt; 0.05.
/// This is a DATA representation, not code — it is the safe rule
/// representation the master prompt requires ("do not execute arbitrary
/// C# or arbitrary code supplied by users").
/// </summary>
public record PolicyCondition(string FieldName, ComparisonOperator Operator, double Value)
{
    public bool Evaluate(IReadOnlyDictionary<string, double> facts)
    {
        if (!facts.TryGetValue(FieldName, out var actual))
            throw new PolicyEvaluationException($"Unknown fact field: '{FieldName}'.");

        return Operator switch
        {
            ComparisonOperator.GreaterThan => actual > Value,
            ComparisonOperator.GreaterThanOrEqual => actual >= Value,
            ComparisonOperator.LessThan => actual < Value,
            ComparisonOperator.LessThanOrEqual => actual <= Value,
            ComparisonOperator.Equal => Math.Abs(actual - Value) < 1e-9,
            ComparisonOperator.NotEqual => Math.Abs(actual - Value) >= 1e-9,
            _ => throw new PolicyEvaluationException($"Unsupported operator: {Operator}")
        };
    }
}

public class PolicyEvaluationException : Exception
{
    public PolicyEvaluationException(string message) : base(message) { }
}
