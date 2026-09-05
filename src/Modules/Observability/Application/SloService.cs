using Atlas.Modules.Observability.Domain;
using Atlas.Modules.Observability.Infrastructure;
using Atlas.Shared.Application;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Observability.Application;

public class SloService : ISloService
{
    private readonly ObservabilityDbContext _db;
    public SloService(ObservabilityDbContext db) => _db = db;

    public async Task<Result<Guid>> DefineSloAsync(Guid organizationId, Guid serviceId, string name, SloMetricType metricType,
        double targetValue, TimeSpan windowDuration, CancellationToken ct = default)
    {
        try
        {
            var slo = ServiceLevelObjective.Create(organizationId, serviceId, name, metricType, targetValue, windowDuration);
            _db.Slos.Add(slo);
            await _db.SaveChangesAsync(ct);
            return Result.Success(slo.Id);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR");
        }
    }

    public async Task RecordOutcomeAsync(Guid organizationId, Guid serviceId, SloMetricType metricType, bool success, CancellationToken ct = default)
    {
        _db.MetricSamples.Add(MetricSample.CreateOutcome(organizationId, serviceId, metricType, success, DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync(ct);
    }

    public async Task RecordLatencyAsync(Guid organizationId, Guid serviceId, SloMetricType metricType, double valueMs, CancellationToken ct = default)
    {
        _db.MetricSamples.Add(MetricSample.CreateLatency(organizationId, serviceId, metricType, valueMs, DateTimeOffset.UtcNow));
        await _db.SaveChangesAsync(ct);
    }

    public async Task<SloComplianceResult?> GetComplianceAsync(Guid organizationId, Guid sloId, CancellationToken ct = default)
    {
        var slo = await _db.Slos.FirstOrDefaultAsync(s => s.Id == sloId && s.OrganizationId == organizationId, ct);
        if (slo is null) return null;

        var windowStart = DateTimeOffset.UtcNow - slo.WindowDuration;
        var samples = await _db.MetricSamples
            .Where(m => m.ServiceId == slo.ServiceId && m.MetricType == slo.MetricType && m.RecordedAtUtc >= windowStart)
            .AsNoTracking()
            .ToListAsync(ct);

        return slo.MetricType switch
        {
            SloMetricType.Availability or SloMetricType.ErrorRate =>
                SloCalculator.CalculateAvailability(samples, slo.TargetValue, slo.WindowDuration),
            SloMetricType.LatencyP95 => SloCalculator.CalculateLatencyPercentile(samples, 95, slo.TargetValue),
            SloMetricType.LatencyP99 => SloCalculator.CalculateLatencyPercentile(samples, 99, slo.TargetValue),
            _ => throw new NotSupportedException($"Unsupported metric type: {slo.MetricType}")
        };
    }

    public async Task<IReadOnlyList<MetricSampleDto>> GetSamplesAsync(Guid organizationId, Guid serviceId, SloMetricType metricType,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default)
    {
        return await _db.MetricSamples.AsNoTracking()
            .Where(m => m.OrganizationId == organizationId && m.ServiceId == serviceId && m.MetricType == metricType
                        && m.RecordedAtUtc >= fromUtc && m.RecordedAtUtc < toUtc)
            .Select(m => new MetricSampleDto(m.Value, m.Success, m.RecordedAtUtc))
            .ToListAsync(ct);
    }
}
