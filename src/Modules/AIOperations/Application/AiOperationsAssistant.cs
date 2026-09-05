using Atlas.Modules.AIOperations.Domain;

namespace Atlas.Modules.AIOperations.Application;

/// <summary>
/// Orchestrates: question -> read-tool calls -> evidence -> answer.
/// This is the real flow from the master prompt's AI Operations spec,
/// minus the natural-language generation step (IAiCompletionClient,
/// planned). Without that client, AnswerAsync composes a deterministic
/// evidence summary rather than fluent prose — still evidence-grounded,
/// still refuses to answer without evidence, just not natural-sounding yet.
/// </summary>
public class AiOperationsAssistant
{
    private readonly IReadOnlyList<IAtlasTool> _readTools;
    private readonly IReadOnlyList<IAtlasTool> _actionTools;
    private readonly IAiCompletionClient? _completionClient;
    private readonly ActionToolGuard _actionGuard;

    public AiOperationsAssistant(IEnumerable<IAtlasTool> tools, ActionToolGuard actionGuard, IAiCompletionClient? completionClient = null)
    {
        var allTools = tools.ToList();
        _readTools = allTools.Where(t => t.Kind == ToolKind.Read).ToList();
        _actionTools = allTools.Where(t => t.Kind == ToolKind.Action).ToList();
        _actionGuard = actionGuard;
        _completionClient = completionClient;
    }

    public async Task<AiAnswer> AnswerAsync(Guid organizationId, string question, IReadOnlyList<string> toolNamesToConsult, CancellationToken ct = default)
    {
        var evidence = new List<Evidence>();

        foreach (var toolName in toolNamesToConsult)
        {
            var tool = _readTools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
            if (tool is null) continue; // unknown tool name is silently skipped, not fabricated

            var result = await tool.InvokeAsync(organizationId, new Dictionary<string, string>(), ct);
            if (result.Success && !string.IsNullOrWhiteSpace(result.Summary))
            {
                evidence.Add(new Evidence(tool.Name, result.Summary, DateTimeOffset.UtcNow));
            }
        }

        if (evidence.Count == 0)
        {
            return AiAnswer.InsufficientEvidence();
        }

        var text = _completionClient is not null
            ? await _completionClient.SummarizeAsync(question, evidence.Select(e => e.Description).ToList(), ct)
            : string.Join(" ", evidence.Select(e => e.Description)); // deterministic fallback, no LLM configured

        return new AiAnswer(true, text, evidence);
    }

    /// <summary>
    /// The ONLY path through which an Action-kind tool can run. Every call
    /// is checked by ActionToolGuard first — a caller that isn't authorized
    /// or hasn't explicitly confirmed gets a failed ToolResult, never a
    /// silent no-op or a bypassed check. Both the outcome and who/what
    /// requested it should be audit-logged by the caller (e.g. the
    /// controller) — this method itself doesn't log, since AIOperations
    /// doesn't own Audit's DbContext (module-boundary rule).
    /// </summary>
    public async Task<ToolResult> ExecuteActionAsync(Guid organizationId, string toolName, IReadOnlyDictionary<string, string> arguments,
        bool callerIsAuthorized, bool explicitConfirmationGiven, CancellationToken ct = default)
    {
        var tool = _actionTools.FirstOrDefault(t => t.Name.Equals(toolName, StringComparison.OrdinalIgnoreCase));
        if (tool is null) return new ToolResult(false, $"No action tool named '{toolName}' is registered.");

        var decision = _actionGuard.Authorize(tool, callerIsAuthorized, explicitConfirmationGiven);
        if (!decision.Authorized) return new ToolResult(false, $"Action denied: {decision.Reason}");

        return await tool.InvokeAsync(organizationId, arguments, ct);
    }
}
