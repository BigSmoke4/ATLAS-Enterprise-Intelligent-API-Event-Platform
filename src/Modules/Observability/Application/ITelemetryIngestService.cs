namespace Atlas.Modules.Observability.Application;

/// <summary>
/// One observed HTTP request. Deliberately a readonly struct in a bounded
/// channel: the request path must never allocate a large object graph or
/// block on the telemetry pipeline.
/// </summary>
public readonly record struct RequestTelemetryObservation(
    Guid OrganizationId,
    Guid? ServiceId,
    string Route,
    string HttpMethod,
    int StatusCode,
    double DurationMs,
    DateTimeOffset ObservedAtUtc,
    string? DeploymentVersion);

public sealed record TelemetryIngestStatistics(long Accepted, long Processed, long Dropped, long Buffered)
{
    public double DropRate => Accepted + Dropped == 0 ? 0d : Dropped / (double)(Accepted + Dropped);
}

/// <summary>
/// Non-blocking ingest seam for request telemetry. Implementations must be
/// safe to call from the request path concurrently and must never throw or
/// block: telemetry is important, but a slow/absent telemetry store must
/// never degrade the platform it observes.
/// </summary>
public interface ITelemetryIngestService
{
    /// <returns>True when the observation was accepted; false when it was dropped because the buffer was saturated.</returns>
    bool TryRecord(in RequestTelemetryObservation observation);

    void MarkProcessed(int count);

    TelemetryIngestStatistics GetStatistics();
}
