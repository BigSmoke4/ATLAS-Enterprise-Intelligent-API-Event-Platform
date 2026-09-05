namespace Atlas.Modules.AIOperations.Domain;

public record AiAnswer(bool HasSufficientEvidence, string Text, IReadOnlyList<Evidence> Citations)
{
    public static AiAnswer InsufficientEvidence() =>
        new(false, "Insufficient evidence.", Array.Empty<Evidence>());
}
