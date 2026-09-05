using Atlas.Modules.AIOperations.Application;
using Atlas.Modules.Observability.Application;

namespace Atlas.Modules.AIOperations.Infrastructure;

/// <summary>
/// Real tool: reads actual SLO compliance for a specific SLO id via
/// ISloService — the caller must pass "sloId" in arguments. Returns an
/// unsuccessful result (not a fabricated number) if the id is missing,
/// unparsable, or not found.
/// </summary>
public class GetSloStatusTool : IAtlasTool
{
    private readonly ISloService _sloService;
    public GetSloStatusTool(ISloService sloService) => _sloService = sloService;

    public string Name => "GetSLOStatus";
    public string Description => "Returns current compliance and error-budget status for a given SLO id (requires 'sloId' argument).";
    public ToolKind Kind => ToolKind.Read;

    public async Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
    {
        if (!arguments.TryGetValue("sloId", out var sloIdRaw) || !Guid.TryParse(sloIdRaw, out var sloId))
            return new ToolResult(false, "No valid 'sloId' argument was provided.");

        var compliance = await _sloService.GetComplianceAsync(organizationId, sloId, ct);
        if (compliance is null)
            return new ToolResult(false, "SLO not found.");

        var summary = compliance.IsCompliant
            ? $"SLO is compliant: current value {compliance.CurrentValue:F4} vs target {compliance.TargetValue:F4}, {compliance.ErrorBudgetRemainingPercent:F1}% error budget remaining ({compliance.SampleCount} samples)."
            : $"SLO is NON-compliant: current value {compliance.CurrentValue:F4} vs target {compliance.TargetValue:F4}, burn rate {compliance.BurnRatePerHour:F4}/hr ({compliance.SampleCount} samples).";

        return new ToolResult(true, summary);
    }
}
