using System.ComponentModel.DataAnnotations.Schema;
using Atlas.Shared.Domain;

namespace Atlas.Modules.Observability.Domain;

/// <summary>
/// One-minute roll-up of live request telemetry for a single
/// (organization, service, route, method, deployment version) tuple.
///
/// This is the entity that makes the "no fabricated metrics" rule
/// enforceable: the dashboard's request rate, error rate and P50/P95/P99 all
/// come from rows like this one, populated by the telemetry middleware and
/// the aggregation background service — there is no code path that writes a
/// human-supplied "current RPS" number.
/// </summary>
public class RequestTelemetryAggregate : TenantEntity
{
    /// <summary>Null when the request could not be attributed to a registered service (e.g. ATLAS's own MVC pages).</summary>
    public Guid? ServiceId { get; private set; }

    /// <summary>Normalized route template (e.g. /api/v1/orders/{id}) — never a raw path with ids in it, so cardinality stays bounded.</summary>
    public string Route { get; private set; } = string.Empty;

    public string HttpMethod { get; private set; } = string.Empty;

    /// <summary>Start of the one-minute bucket (UTC, truncated to the minute).</summary>
    public DateTimeOffset WindowStartUtc { get; private set; }

    /// <summary>
    /// The service version that was live during this bucket, resolved from
    /// DeploymentIntelligence. Empty string means "no deployment on record" —
    /// deliberately not nullable so the uniqueness constraint on the bucket
    /// key behaves predictably in PostgreSQL (NULLs compare as distinct).
    /// </summary>
    public string DeploymentVersion { get; private set; } = string.Empty;

    public long RequestCount { get; private set; }
    public long ServerErrorCount { get; private set; }
    public long ClientErrorCount { get; private set; }
    public double TotalDurationMs { get; private set; }
    public double MaxDurationMs { get; private set; }

    /// <summary>Fixed-bucket latency histogram (see <see cref="LatencyHistogram"/>) stored as a PostgreSQL bigint[].</summary>
    public long[] DurationBucketCounts { get; private set; } = LatencyHistogram.CreateEmptyBucketCounts();

    private RequestTelemetryAggregate() { }

    public static RequestTelemetryAggregate Create(Guid organizationId, DateTimeOffset windowStartUtc, Guid? serviceId,
        string route, string httpMethod, string? deploymentVersion)
        => new()
        {
            OrganizationId = organizationId,
            WindowStartUtc = TruncateToMinute(windowStartUtc),
            ServiceId = serviceId,
            Route = string.IsNullOrWhiteSpace(route) ? "/" : route,
            HttpMethod = string.IsNullOrWhiteSpace(httpMethod) ? "GET" : httpMethod.ToUpperInvariant(),
            DeploymentVersion = deploymentVersion ?? string.Empty
        };

    public void Apply(int statusCode, double durationMs)
    {
        if (durationMs < 0) durationMs = 0;

        RequestCount++;
        if (statusCode >= 500) ServerErrorCount++;
        else if (statusCode >= 400) ClientErrorCount++;

        TotalDurationMs += durationMs;
        if (durationMs > MaxDurationMs) MaxDurationMs = durationMs;

        // Copy-on-write: EF Core compares array properties by reference, so
        // mutating the existing array in place would be invisible to change
        // tracking (the classic silently-lost-update bug).
        var buckets = (long[])DurationBucketCounts.Clone();
        buckets[LatencyHistogram.BucketIndex(durationMs)]++;
        DurationBucketCounts = buckets;

        Touch();
    }

    /// <summary>Merges a pre-aggregated batch (used by the flush service so one round trip updates a whole minute).</summary>
    public void Merge(long requestCount, long serverErrors, long clientErrors, double totalDurationMs, double maxDurationMs, IReadOnlyList<long> bucketCounts)
    {
        RequestCount += requestCount;
        ServerErrorCount += serverErrors;
        ClientErrorCount += clientErrors;
        TotalDurationMs += totalDurationMs;
        if (maxDurationMs > MaxDurationMs) MaxDurationMs = maxDurationMs;

        var buckets = (long[])DurationBucketCounts.Clone();
        LatencyHistogram.Add(bucketCounts, buckets);
        DurationBucketCounts = buckets;

        Touch();
    }

    [NotMapped]
    public long ErrorCount => ServerErrorCount;

    [NotMapped]
    public double? AverageDurationMs => RequestCount == 0 ? null : TotalDurationMs / RequestCount;

    [NotMapped]
    public double? P50Ms => LatencyHistogram.PercentileMs(DurationBucketCounts, 50);

    [NotMapped]
    public double? P95Ms => LatencyHistogram.PercentileMs(DurationBucketCounts, 95);

    [NotMapped]
    public double? P99Ms => LatencyHistogram.PercentileMs(DurationBucketCounts, 99);

    public static DateTimeOffset TruncateToMinute(DateTimeOffset value)
        => new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, TimeSpan.Zero);
}
