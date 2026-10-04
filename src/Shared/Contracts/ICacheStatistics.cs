namespace Atlas.Shared.Contracts;

/// <summary>
/// Counter snapshot for a cache implementation. Kept as a contract (not a
/// static counter) so the numbers are per-container, testable, and can be
/// exported as Prometheus gauges without a global mutable singleton.
/// </summary>
public sealed record CacheStatisticsSnapshot(long Hits, long Misses, long Sets, long Invalidations, long Failures)
{
    public long Lookups => Hits + Misses;

    /// <summary>Hit rate in the range 0..1; 0 when no lookups have been recorded yet.</summary>
    public double HitRate => Lookups == 0 ? 0d : Hits / (double)Lookups;
}

public interface ICacheStatistics
{
    void RecordHit();
    void RecordMiss();
    void RecordSet();
    void RecordInvalidation();
    void RecordFailure();
    CacheStatisticsSnapshot Snapshot();
}

/// <summary>
/// Read seam that lets the telemetry pipeline attribute an observation to the
/// service version that was live at that moment (DeploymentIntelligence owns
/// the deployment history, so it implements this; Observability only consumes
/// the contract). Returns null when no deployment is on record — callers must
/// treat null as "unknown", never as an empty version string.
/// </summary>
public interface IActiveDeploymentVersionProvider
{
    Task<string?> GetActiveVersionAsync(Guid organizationId, Guid serviceId, DateTimeOffset atUtc, CancellationToken ct = default);
}
