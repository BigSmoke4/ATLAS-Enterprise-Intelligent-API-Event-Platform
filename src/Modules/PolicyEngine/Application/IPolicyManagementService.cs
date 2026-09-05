using Atlas.Modules.PolicyEngine.Domain;

namespace Atlas.Modules.PolicyEngine.Application;

public interface IPolicyManagementService
{
    Task<PolicyOperationResult<Guid>> CreateAsync(Guid organizationId, string name, IReadOnlyList<PolicyCondition> conditions, PolicyAction action, CancellationToken ct = default);
    Task<PolicyOperationResult> DeactivateAsync(Guid organizationId, Guid policyId, CancellationToken ct = default);
    Task<IReadOnlyList<PolicyRule>> ListAsync(Guid organizationId, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<IReadOnlyList<PolicyEvaluationOutcome>> EvaluateActiveRulesAsync(Guid organizationId, IReadOnlyDictionary<string, double> facts, CancellationToken ct = default);
}

public record PolicyOperationResult(bool Success, string? Error = null)
{
    public static PolicyOperationResult Ok() => new(true);
    public static PolicyOperationResult Fail(string error) => new(false, error);
}

public record PolicyOperationResult<T>(bool Success, T? Value, string? Error)
{
    public static PolicyOperationResult<T> Ok(T value) => new(true, value, null);
    public static PolicyOperationResult<T> Fail(string error) => new(false, default, error);
}
