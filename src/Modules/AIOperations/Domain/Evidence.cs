namespace Atlas.Modules.AIOperations.Domain;

/// <summary>
/// One fact obtained from a real ATLAS tool call. Every claim in an
/// AiAnswer must trace back to at least one Evidence item — this is the
/// data structure that makes "the AI must cite the actual ATLAS evidence
/// used" (master prompt) mechanically enforceable rather than a prompting
/// convention.
/// </summary>
public record Evidence(string ToolName, string Description, DateTimeOffset ObservedAtUtc);
