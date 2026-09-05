namespace Atlas.Modules.AIOperations.Application;

public enum ToolKind { Read, Action }

/// <summary>
/// A single callable capability the AI can invoke — GetServiceHealth,
/// GetSLOStatus, GetIncidentHistory, etc. Read tools may be invoked freely;
/// Action tools (none implemented yet) must go through explicit
/// authorization + confirmation per the master prompt's AI-safety section —
/// see ActionToolGuard.
/// </summary>
public interface IAtlasTool
{
    string Name { get; }
    string Description { get; }
    ToolKind Kind { get; }

    Task<ToolResult> InvokeAsync(Guid organizationId, IReadOnlyDictionary<string, string> arguments, CancellationToken ct = default);
}

public record ToolResult(bool Success, string Summary, IReadOnlyDictionary<string, double>? Facts = null);
