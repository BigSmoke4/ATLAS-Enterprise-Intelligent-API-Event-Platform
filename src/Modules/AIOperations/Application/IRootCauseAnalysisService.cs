namespace Atlas.Modules.AIOperations.Application;

public interface IRootCauseAnalysisService
{
    Task<RootCauseAnalysisResult> AnalyzeIncidentAsync(Guid organizationId, Guid incidentId, CancellationToken ct = default);
}

public sealed record RootCauseEvidence(string Source, string Observation, DateTimeOffset? ObservedAtUtc = null);
public sealed record RootCauseAnalysisResult(bool AnalysisAvailable, string Conclusion, double Confidence, IReadOnlyList<Guid> AffectedComponents, IReadOnlyList<RootCauseEvidence> Evidence);
