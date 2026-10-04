namespace Atlas.Modules.Observability.Application;

/// <summary>
/// Read model over live request telemetry (the one-minute
/// <c>RequestTelemetryAggregate</c> buckets). Every number returned here is
/// aggregated from recorded rows; when a window has no rows the DTOs report
/// <c>HasData = false</c> / null percentiles rather than zeroes, so the UI can
/// say "No telemetry available." honestly.
/// </summary>
public interface ITelemetryQueryService
{
    Task<PlatformTrafficSummary> GetPlatformSummaryAsync(Guid organizationId, TimeSpan window, CancellationToken ct = default);

    Task<IReadOnlyList<ServiceTrafficSummary>> GetServiceSummariesAsync(Guid organizationId, TimeSpan window, CancellationToken ct = default);

    Task<IReadOnlyList<RouteTrafficSummary>> GetRouteSummariesAsync(Guid organizationId, TimeSpan window, Guid? serviceId = null, int limit = 50, CancellationToken ct = default);

    Task<IReadOnlyList<TrafficSeriesPoint>> GetSeriesAsync(Guid organizationId, TimeSpan window, TimeSpan bucketSize, Guid? serviceId = null, CancellationToken ct = default);

    /// <summary>Most recent bucket timestamp ATLAS has recorded for the organization — null when nothing was ever recorded.</summary>
    Task<DateTimeOffset?> GetLastRecordedAtUtcAsync(Guid organizationId, CancellationToken ct = default);
}

public sealed record PlatformTrafficSummary(
    bool HasData,
    long Requests,
    long ServerErrors,
    long ClientErrors,
    double ErrorRate,
    double RequestsPerSecond,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms,
    double? AverageMs,
    double MaxMs,
    int ServiceCount,
    int RouteCount,
    DateTimeOffset? FirstBucketUtc,
    DateTimeOffset? LastBucketUtc,
    TimeSpan Window);

public sealed record ServiceTrafficSummary(
    Guid ServiceId,
    long Requests,
    long ServerErrors,
    double ErrorRate,
    double RequestsPerSecond,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms,
    double? AverageMs,
    string? DeploymentVersion,
    DateTimeOffset LastBucketUtc);

public sealed record RouteTrafficSummary(
    Guid? ServiceId,
    string Route,
    string HttpMethod,
    long Requests,
    long ServerErrors,
    long ClientErrors,
    double ErrorRate,
    double? P95Ms,
    double? P99Ms,
    DateTimeOffset LastBucketUtc);

public sealed record TrafficSeriesPoint(
    DateTimeOffset BucketStartUtc,
    long Requests,
    long ServerErrors,
    long ClientErrors,
    double ErrorRate,
    double? P95Ms,
    bool HasData);
