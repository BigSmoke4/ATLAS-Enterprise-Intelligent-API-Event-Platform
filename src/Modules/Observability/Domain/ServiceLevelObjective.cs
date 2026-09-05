using Atlas.Shared.Domain;

namespace Atlas.Modules.Observability.Domain;

public enum SloMetricType { Availability, LatencyP95, LatencyP99, ErrorRate }

/// <summary>
/// A configured target (e.g. 99.95% availability, P95 &lt; 300ms). Compliance
/// and error-budget burn are always computed from recorded MetricSample
/// rows, never fabricated — see SloCalculator.
/// </summary>
public class ServiceLevelObjective : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public Guid ServiceId { get; private set; }
    public SloMetricType MetricType { get; private set; }

    /// <summary>Target as a ratio, e.g. 0.9995 for 99.95% availability, or 300 for a 300ms latency threshold.</summary>
    public double TargetValue { get; private set; }
    public TimeSpan WindowDuration { get; private set; }

    private ServiceLevelObjective() { }

    public static ServiceLevelObjective Create(Guid organizationId, Guid serviceId, string name,
        SloMetricType metricType, double targetValue, TimeSpan windowDuration)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("SLO name is required.", nameof(name));
        if (windowDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(windowDuration));

        return new ServiceLevelObjective
        {
            OrganizationId = organizationId, ServiceId = serviceId, Name = name,
            MetricType = metricType, TargetValue = targetValue, WindowDuration = windowDuration
        };
    }
}
