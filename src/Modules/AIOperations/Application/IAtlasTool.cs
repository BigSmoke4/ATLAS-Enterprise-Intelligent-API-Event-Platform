namespace Atlas.Modules.AIOperations.Application;

public enum ToolKind { Read, Action }

/// <summary>
/// A single callable capability the AI can invoke — GetServiceHealth,
/// GetSLOStatus, GetIncidentHistory, etc. Read tools may be invoked freely;
/// Action tools (DeactivatePolicy today) must go through explicit
/// authorization + confirmation — see ActionToolGuard, which the orchestrator
/// checks before running anything of kind <see cref="ToolKind.Action"/>.
/// </summary>
public interface IAtlasTool
{
    string Name { get; }
    string Description { get; }
    ToolKind Kind { get; }

    Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default);
}

public record ToolResult(bool Success, string Summary, IReadOnlyDictionary<string, double>? Facts = null);
