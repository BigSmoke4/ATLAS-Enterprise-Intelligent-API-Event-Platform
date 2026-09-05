namespace Atlas.Modules.AIOperations.Application;

/// <summary>
/// PLANNED integration point for an actual LLM call (e.g. the Anthropic
/// API) that turns a question + gathered Evidence into prose. Deliberately
/// not implemented here: no API key management/config exists yet, and per
/// the "No Fake Functionality" rule this must not be stubbed to return
/// canned text. AiOperationsAssistant (below) works correctly without this
/// — it composes a deterministic, evidence-grounded summary — but a real
/// natural-language answer needs this wired up.
/// </summary>
public interface IAiCompletionClient
{
    Task<string> SummarizeAsync(string question, IReadOnlyList<string> evidenceDescriptions, CancellationToken ct = default);
}
