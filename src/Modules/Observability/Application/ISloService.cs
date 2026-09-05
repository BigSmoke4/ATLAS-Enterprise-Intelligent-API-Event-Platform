using Atlas.Modules.Observability.Domain;
using Atlas.Shared.Application;

namespace Atlas.Modules.Observability.Application;

public interface ISloService
{
    Task<Result<Guid>> DefineSloAsync(Guid organizationId, Guid serviceId, string name, SloMetricType metricType,
        double targetValue, TimeSpan windowDuration, CancellationToken ct = default);

    Task RecordOutcomeAsync(Guid organizationId, Guid serviceId, SloMetricType metricType, bool success, CancellationToken ct = default);
    Task RecordLatencyAsync(Guid organizationId, Guid serviceId, SloMetricType metricType, double valueMs, CancellationToken ct = default);

    /// <returns>Compliance/error-budget result, or null if no SLO with that id exists for the organization.</returns>
    Task<SloComplianceResult?> GetComplianceAsync(Guid organizationId, Guid sloId, CancellationToken ct = default);

    /// <summary>
    /// Read-only access to raw samples in a time window — the seam other
    /// modules (e.g. DeploymentIntelligence's regression analysis) use
    /// instead of referencing ObservabilityDbContext directly, preserving
    /// the "no module touches another module's DbContext" boundary rule.
    /// </summary>
    Task<IReadOnlyList<MetricSampleDto>> GetSamplesAsync(Guid organizationId, Guid serviceId, SloMetricType metricType,
        DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct = default);
}

public record MetricSampleDto(double Value, bool? Success, DateTimeOffset RecordedAtUtc);
