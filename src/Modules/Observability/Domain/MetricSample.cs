using Atlas.Shared.Domain;

namespace Atlas.Modules.Observability.Domain;

/// <summary>
/// A single recorded observation (e.g. one request's latency, or one
/// success/failure outcome). SLO compliance is always aggregated from a set
/// of these — there is intentionally no path to set a "current compliance"
/// number directly.
/// </summary>
public class MetricSample : TenantEntity
{
    public Guid ServiceId { get; private set; }
    public SloMetricType MetricType { get; private set; }
    public double Value { get; private set; }
    public bool? Success { get; private set; } // used for ErrorRate/Availability samples
    public DateTimeOffset RecordedAtUtc { get; private set; }

    private MetricSample() { }

    public static MetricSample CreateLatency(Guid organizationId, Guid serviceId, SloMetricType metricType, double valueMs, DateTimeOffset recordedAtUtc)
        => new() { OrganizationId = organizationId, ServiceId = serviceId, MetricType = metricType, Value = valueMs, RecordedAtUtc = recordedAtUtc };

    public static MetricSample CreateOutcome(Guid organizationId, Guid serviceId, SloMetricType metricType, bool success, DateTimeOffset recordedAtUtc)
        => new() { OrganizationId = organizationId, ServiceId = serviceId, MetricType = metricType, Success = success, Value = success ? 1 : 0, RecordedAtUtc = recordedAtUtc };
}
