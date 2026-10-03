using Atlas.Modules.AIOperations.Application;

namespace Atlas.Modules.AIOperations.Infrastructure;

public sealed class GetRootCauseAnalysisTool : IAtlasTool
{
    private readonly IRootCauseAnalysisService _analysis;
    public GetRootCauseAnalysisTool(IRootCauseAnalysisService analysis) => _analysis = analysis;
    public string Name => "GetRootCauseAnalysis";
    public string Description => "Correlates stored incident, deployment, service health, error, and latency evidence with an explainable confidence score.";
    public ToolKind Kind => ToolKind.Read;

    public async Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default)
    {
        if (!arguments.TryGetValue("incidentId", out var raw) || !Guid.TryParse(raw, out var incidentId))
            return new ToolResult(false, "incidentId is required.");
        var result = await _analysis.AnalyzeIncidentAsync(organizationId, incidentId, ct);
        var evidence = string.Join(" ", result.Evidence.Select(e => e.Observation));
        return new ToolResult(result.AnalysisAvailable, $"{result.Conclusion} Confidence: {result.Confidence:P0}. Evidence: {evidence}");
    }
}
