using Atlas.Modules.AIOperations.Application;
using Atlas.Modules.PolicyEngine.Application;

namespace Atlas.Modules.AIOperations.Infrastructure;

/// <summary>
/// The first real Action-kind tool: deactivates a PolicyEngine rule.
/// Genuinely destructive (a deactivated policy stops firing), so it must go
/// through AiOperationsAssistant.ExecuteActionAsync -> ActionToolGuard,
/// never the plain evidence-gathering AnswerAsync path. Requires a
/// "policyId" argument (a GUID string).
/// </summary>
public class DeactivatePolicyTool : IAtlasTool
{
    private readonly IPolicyManagementService _policies;
    public DeactivatePolicyTool(IPolicyManagementService policies) => _policies = policies;

    public string Name => "DeactivatePolicy";
    public string Description => "Deactivates a PolicyEngine rule by id (requires 'policyId' argument). Destructive — requires authorization and explicit confirmation.";
    public ToolKind Kind => ToolKind.Action;

    public async Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
    {
        if (!arguments.TryGetValue("policyId", out var raw) || !Guid.TryParse(raw, out var policyId))
            return new ToolResult(false, "No valid 'policyId' argument was provided.");

        var result = await _policies.DeactivateAsync(organizationId, policyId, ct);
        return new ToolResult(result.Success, result.Success ? $"Policy {policyId} deactivated." : result.Error ?? "Failed to deactivate policy.");
    }
}
