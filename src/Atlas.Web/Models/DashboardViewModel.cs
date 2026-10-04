namespace Atlas.Web.Models;

/// <summary>
/// Command-center view model. Every section is populated from recorded
/// application data (service registry, incident store, SLO definitions +
/// compliance derived from samples, deployment history). Any section whose
/// data is absent renders "No telemetry available." rather than a
/// placeholder value.
/// </summary>
public sealed class DashboardViewModel
{
    public bool HasOrganizationContext { get; set; }

    public int ServiceTotal { get; set; }
    public int ServiceHealthy { get; set; }
    public int ServiceDegraded { get; set; }
    public int ServiceUnhealthy { get; set; }
    public int ServiceUnavailable { get; set; }

    public IReadOnlyList<IncidentSummary> ActiveIncidents { get; set; } = Array.Empty<IncidentSummary>();
    public IReadOnlyList<SloSummary> Slos { get; set; } = Array.Empty<SloSummary>();
    public IReadOnlyList<DeploymentSummary> RecentDeployments { get; set; } = Array.Empty<DeploymentSummary>();

    /// <summary>Un-replayed dead-letter events across all Kafka topics (platform-level, not tenant-scoped).</summary>
    public int DeadLetterCount { get; set; }
    public IReadOnlyList<SecurityEventSummary> SecurityEvents { get; set; } = Array.Empty<SecurityEventSummary>();
}

public sealed record IncidentSummary(Guid IncidentId, string Title, string Severity, string Status, DateTimeOffset StartedAtUtc);

public sealed record SloSummary(Guid SloId, string Name, string MetricType, double TargetValue,
    double? CurrentValue, bool? IsCompliant, double? ErrorBudgetRemainingPercent, int SampleCount);

public sealed record DeploymentSummary(Guid DeploymentId, Guid ServiceId, string Version, string Environment,
    string Author, DateTimeOffset DeployedAtUtc, string Status);

public sealed record SecurityEventSummary(string Action, string ResourceType, string ActorDisplay, DateTimeOffset RecordedAtUtc);
