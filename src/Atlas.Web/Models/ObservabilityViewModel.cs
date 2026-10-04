namespace Atlas.Web.Models;

/// <summary>
/// Observability/SLO page view model. Rows are real SLO definitions from
/// ISloService with compliance/error-budget math derived from recorded
/// metric samples (never fabricated — no samples means "no data yet").
/// </summary>
public sealed class ObservabilityViewModel
{
    public bool HasOrganizationContext { get; set; }
    public IReadOnlyList<SloDetailRow> Slos { get; set; } = Array.Empty<SloDetailRow>();
}

public sealed record SloDetailRow(
    Guid SloId,
    Guid ServiceId,
    string Name,
    string MetricType,
    double TargetValue,
    TimeSpan WindowDuration,
    double? CurrentValue,
    bool? IsCompliant,
    double? ErrorBudgetRemainingPercent,
    int SampleCount);
