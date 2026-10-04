using Atlas.Modules.Observability.Domain;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Observability.Application;

/// <summary>
/// Aggregates the stored one-minute buckets into the read models the
/// dashboard, the observability page and <c>/api/v1/metrics</c> consume.
///
/// All aggregation happens in memory over a bounded row set (one row per
/// minute per route per version), never by loading a table: the query is
/// always constrained by organization + time window + optional service.
/// </summary>
public sealed class TelemetryQueryService : ITelemetryQueryService
{
    private readonly ObservabilityDbContext _db;

    public TelemetryQueryService(ObservabilityDbContext db) => _db = db;

    public async Task<PlatformTrafficSummary> GetPlatformSummaryAsync(Guid organizationId, TimeSpan window, CancellationToken ct = default)
    {
        var (from, to) = ResolveWindow(window);
        var rows = await QueryAsync(organizationId, from, to, serviceId: null, ct);
        return Summarize(rows, window, from);
    }

    public async Task<IReadOnlyList<ServiceTrafficSummary>> GetServiceSummariesAsync(Guid organizationId, TimeSpan window, CancellationToken ct = default)
    {
        var (from, to) = ResolveWindow(window);
        var rows = await QueryAsync(organizationId, from, to, serviceId: null, ct);

        return rows
            .Where(r => r.ServiceId.HasValue)
            .GroupBy(r => r.ServiceId!.Value)
            .Select(group =>
            {
                var list = group.ToList();
                var requests = list.Sum(r => r.RequestCount);
                var errors = list.Sum(r => r.ServerErrorCount);
                var buckets = Combine(list);
                var version = list.Where(r => !string.IsNullOrWhiteSpace(r.DeploymentVersion))
                    .OrderByDescending(r => r.WindowStartUtc)
                    .Select(r => r.DeploymentVersion!)
                    .FirstOrDefault();

                return new ServiceTrafficSummary(
                    group.Key,
                    requests,
                    errors,
                    requests == 0 ? 0d : errors / (double)requests,
                    requests / Math.Max(1, window.TotalSeconds),
                    LatencyHistogram.PercentileMs(buckets, 50),
                    LatencyHistogram.PercentileMs(buckets, 95),
                    LatencyHistogram.PercentileMs(buckets, 99),
                    requests == 0 ? null : list.Sum(r => r.TotalDurationMs) / requests,
                    version,
                    list.Max(r => r.WindowStartUtc));
            })
            .OrderByDescending(s => s.Requests)
            .ToList();
    }

    public async Task<IReadOnlyList<RouteTrafficSummary>> GetRouteSummariesAsync(Guid organizationId, TimeSpan window, Guid? serviceId = null, int limit = 50, CancellationToken ct = default)
    {
        var (from, to) = ResolveWindow(window);
        var rows = await QueryAsync(organizationId, from, to, serviceId, ct);

        return rows
            .GroupBy(r => new { r.ServiceId, r.Route, r.HttpMethod })
            .Select(group =>
            {
                var list = group.ToList();
                var requests = list.Sum(r => r.RequestCount);
                var errors = list.Sum(r => r.ServerErrorCount);
                var buckets = Combine(list);
                return new RouteTrafficSummary(
                    group.Key.ServiceId, group.Key.Route, group.Key.HttpMethod,
                    requests, errors, list.Sum(r => r.ClientErrorCount),
                    requests == 0 ? 0d : errors / (double)requests,
                    LatencyHistogram.PercentileMs(buckets, 95),
                    LatencyHistogram.PercentileMs(buckets, 99),
                    list.Max(r => r.WindowStartUtc));
            })
            .OrderByDescending(r => r.Requests)
            .Take(Math.Clamp(limit, 1, 200))
            .ToList();
    }

    public async Task<IReadOnlyList<TrafficSeriesPoint>> GetSeriesAsync(Guid organizationId, TimeSpan window, TimeSpan bucketSize, Guid? serviceId = null, CancellationToken ct = default)
    {
        var (from, to) = ResolveWindow(window);
        var rows = await QueryAsync(organizationId, from, to, serviceId, ct);

        var size = bucketSize <= TimeSpan.Zero ? TimeSpan.FromMinutes(1) : bucketSize;
        var bucketCount = (int)Math.Ceiling(window.TotalSeconds / size.TotalSeconds);
        bucketCount = Math.Clamp(bucketCount, 1, 720);

        // Pre-seed every bucket so the chart shows real gaps instead of
        // silently compressing empty periods (a chart that hides outages is
        // worse than no chart).
        var points = new List<TrafficSeriesPoint>(bucketCount);
        var opened = new Dictionary<long, BucketAccumulator>();

        foreach (var row in rows)
        {
            var index = (long)Math.Floor((row.WindowStartUtc - from).TotalSeconds / size.TotalSeconds);
            if (index < 0 || index >= bucketCount) continue;
            if (!opened.TryGetValue(index, out var accumulator))
            {
                accumulator = new BucketAccumulator();
                opened[index] = accumulator;
            }
            accumulator.Add(row);
        }

        for (var i = 0; i < bucketCount; i++)
        {
            var bucketStart = from + TimeSpan.FromTicks(size.Ticks * i);
            if (opened.TryGetValue(i, out var accumulator))
            {
                var requests = accumulator.Requests;
                points.Add(new TrafficSeriesPoint(bucketStart, requests, accumulator.ServerErrors, accumulator.ClientErrors,
                    requests == 0 ? 0d : accumulator.ServerErrors / (double)requests,
                    LatencyHistogram.PercentileMs(accumulator.Buckets, 95),
                    HasData: true));
            }
            else
            {
                points.Add(new TrafficSeriesPoint(bucketStart, 0, 0, 0, 0d, null, HasData: false));
            }
        }

        return points;
    }

    public async Task<DateTimeOffset?> GetLastRecordedAtUtcAsync(Guid organizationId, CancellationToken ct = default)
    {
        var query = _db.RequestTelemetryAggregates.AsNoTracking().Where(a => a.OrganizationId == organizationId);
        if (!await query.AnyAsync(ct)) return null;
        return await query.MaxAsync(a => a.WindowStartUtc, ct);
    }

    private async Task<List<RequestTelemetryAggregate>> QueryAsync(Guid organizationId, DateTimeOffset from, DateTimeOffset to, Guid? serviceId, CancellationToken ct)
    {
        var query = _db.RequestTelemetryAggregates.AsNoTracking()
            .Where(a => a.OrganizationId == organizationId && a.WindowStartUtc >= from && a.WindowStartUtc <= to);
        if (serviceId.HasValue) query = query.Where(a => a.ServiceId == serviceId.Value);
        return await query.ToListAsync(ct);
    }

    private static (DateTimeOffset From, DateTimeOffset To) ResolveWindow(TimeSpan window)
    {
        var effective = window <= TimeSpan.Zero ? TimeSpan.FromHours(1) : window;
        var now = DateTimeOffset.UtcNow;
        return (now - effective, now);
    }

    private static PlatformTrafficSummary Summarize(IReadOnlyList<RequestTelemetryAggregate> rows, TimeSpan window, DateTimeOffset from)
    {
        if (rows.Count == 0)
        {
            return new PlatformTrafficSummary(false, 0, 0, 0, 0d, 0d, null, null, null, null, 0, 0, 0, null, null, window);
        }

        var requests = rows.Sum(r => r.RequestCount);
        var serverErrors = rows.Sum(r => r.ServerErrorCount);
        var clientErrors = rows.Sum(r => r.ClientErrorCount);
        var buckets = Combine(rows);

        return new PlatformTrafficSummary(
            true,
            requests,
            serverErrors,
            clientErrors,
            requests == 0 ? 0d : serverErrors / (double)requests,
            requests / Math.Max(1, window.TotalSeconds),
            LatencyHistogram.PercentileMs(buckets, 50),
            LatencyHistogram.PercentileMs(buckets, 95),
            LatencyHistogram.PercentileMs(buckets, 99),
            requests == 0 ? null : rows.Sum(r => r.TotalDurationMs) / requests,
            rows.Max(r => r.MaxDurationMs),
            rows.Where(r => r.ServiceId.HasValue).Select(r => r.ServiceId!.Value).Distinct().Count(),
            rows.Select(r => r.Route).Distinct().Count(),
            rows.Min(r => r.WindowStartUtc),
            rows.Max(r => r.WindowStartUtc),
            window);
    }

    private static long[] Combine(IReadOnlyList<RequestTelemetryAggregate> rows)
    {
        var combined = LatencyHistogram.CreateEmptyBucketCounts();
        foreach (var row in rows) LatencyHistogram.Add(row.DurationBucketCounts, combined);
        return combined;
    }

    private sealed class BucketAccumulator
    {
        public long Requests { get; private set; }
        public long ServerErrors { get; private set; }
        public long ClientErrors { get; private set; }
        public long[] Buckets { get; } = LatencyHistogram.CreateEmptyBucketCounts();

        public void Add(RequestTelemetryAggregate row)
        {
            Requests += row.RequestCount;
            ServerErrors += row.ServerErrorCount;
            ClientErrors += row.ClientErrorCount;
            LatencyHistogram.Add(row.DurationBucketCounts, Buckets);
        }
    }
}
