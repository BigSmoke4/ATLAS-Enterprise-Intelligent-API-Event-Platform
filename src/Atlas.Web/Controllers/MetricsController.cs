using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.Observability.Application;
using Atlas.Shared.Contracts;
using Atlas.Web.Observability;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>
/// Read-only metrics surface (v1). Thin controller: every response is produced
/// by an application service or an infrastructure probe — no aggregation,
/// querying or math happens here.
///
/// Every endpoint reports its own evidence state (<c>hasData</c>/<c>available</c>
/// plus a reason) so a client can render "No telemetry available." instead of
/// mistaking an empty result for zero traffic.
/// </summary>
[ApiController]
[Route("api/v1/metrics")]
[Authorize]
public sealed class MetricsController : ControllerBase
{
    private readonly ITelemetryQueryService _telemetry;
    private readonly ITelemetryIngestService _ingest;
    private readonly ICacheStatistics _cacheStatistics;
    private readonly IDatabaseMetricsProbe _database;
    private readonly IConsumerLagService _consumerLag;

    public MetricsController(
        ITelemetryQueryService telemetry,
        ITelemetryIngestService ingest,
        ICacheStatistics cacheStatistics,
        IDatabaseMetricsProbe database,
        IConsumerLagService consumerLag)
    {
        _telemetry = telemetry;
        _ingest = ingest;
        _cacheStatistics = cacheStatistics;
        _database = database;
        _consumerLag = consumerLag;
    }

    [HttpGet("summary")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<PlatformTrafficSummary>> Summary([FromQuery] Guid organizationId,
        [FromQuery] int windowMinutes = 60, CancellationToken ct = default)
        => Ok(await _telemetry.GetPlatformSummaryAsync(organizationId, Window(windowMinutes), ct));

    [HttpGet("services")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<ServiceTrafficSummary>>> Services([FromQuery] Guid organizationId,
        [FromQuery] int windowMinutes = 60, CancellationToken ct = default)
        => Ok(await _telemetry.GetServiceSummariesAsync(organizationId, Window(windowMinutes), ct));

    [HttpGet("routes")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<RouteTrafficSummary>>> Routes([FromQuery] Guid organizationId,
        [FromQuery] Guid? serviceId, [FromQuery] int windowMinutes = 60, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(await _telemetry.GetRouteSummariesAsync(organizationId, Window(windowMinutes), serviceId, limit, ct));

    [HttpGet("series")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<ActionResult<IReadOnlyList<TrafficSeriesPoint>>> Series([FromQuery] Guid organizationId,
        [FromQuery] Guid? serviceId, [FromQuery] int windowMinutes = 60, [FromQuery] int bucketMinutes = 5, CancellationToken ct = default)
        => Ok(await _telemetry.GetSeriesAsync(organizationId, Window(windowMinutes), TimeSpan.FromMinutes(Math.Clamp(bucketMinutes, 1, 60)), serviceId, ct));

    /// <summary>Infrastructure-level metrics: PostgreSQL, Redis cache, telemetry pipeline, Kafka consumer lag.</summary>
    [HttpGet("infrastructure")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> Infrastructure([FromQuery] Guid organizationId, CancellationToken ct = default)
    {
        var database = await _database.SnapshotAsync(ct);
        var cache = _cacheStatistics.Snapshot();
        var ingest = _ingest.GetStatistics();
        var lag = _consumerLag.GetSnapshot();

        return Ok(new
        {
            organizationId,
            capturedAtUtc = DateTimeOffset.UtcNow,
            database = database is null
                ? new { available = false, reason = "PostgreSQL statistics could not be read." }
                : new
                {
                    available = true,
                    database.TotalConnections,
                    database.ActiveConnections,
                    database.IdleConnections,
                    database.IdleInTransactionConnections,
                    database.MaxConnections,
                    database.ConnectionUtilization,
                    database.LongestActiveQuerySeconds,
                    database.TransactionsCommitted,
                    database.TransactionsRolledBack,
                    database.BlockCacheHitRatio,
                    database.Deadlocks,
                    database.CapturedAtUtc
                },
            cache = new
            {
                available = true,
                cache.Hits,
                cache.Misses,
                cache.Lookups,
                cache.HitRate,
                cache.Sets,
                cache.Invalidations,
                cache.Failures,
                backend = "redis"
            },
            telemetryPipeline = new
            {
                available = ingest.Accepted > 0,
                ingest.Accepted,
                ingest.Processed,
                ingest.Dropped,
                ingest.Buffered,
                ingest.DropRate,
                storage = "postgresql:observability.RequestTelemetryAggregates (1-minute buckets)"
            },
            consumerLag = new
            {
                lag.Available,
                lag.Reason,
                lag.ConsumerGroup,
                lag.ReportedAtUtc,
                lag.TotalLag,
                partitions = lag.Partitions.Select(p => new { p.Topic, p.Partition, p.CommittedOffset, p.HighWatermark, p.Lag, p.Available })
            }
        });
    }

    private static TimeSpan Window(int windowMinutes) => TimeSpan.FromMinutes(Math.Clamp(windowMinutes, 1, 60 * 24 * 7));
}
