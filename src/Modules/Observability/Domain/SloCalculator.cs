namespace Atlas.Modules.Observability.Domain;

public record SloComplianceResult(
    double CurrentValue,
    double TargetValue,
    bool IsCompliant,
    double ErrorBudgetTotal,
    double ErrorBudgetConsumed,
    double ErrorBudgetRemainingPercent,
    double BurnRatePerHour,
    int SampleCount);

/// <summary>
/// Pure functions over recorded samples — the only place SLO compliance and
/// error-budget numbers are computed. No caller may report a compliance
/// percentage that didn't come through here, per the master prompt's
/// "derive from recorded telemetry, do not fabricate" requirement.
/// </summary>
public static class SloCalculator
{
    /// <summary>For Availability/ErrorRate SLOs: samples must all have Success set.</summary>
    public static SloComplianceResult CalculateAvailability(IReadOnlyList<MetricSample> samples, double targetRatio, TimeSpan window)
    {
        if (samples.Count == 0)
        {
            return new SloComplianceResult(0, targetRatio, false, 0, 0, 100, 0, 0);
        }

        var successCount = samples.Count(s => s.Success == true);
        var currentRatio = successCount / (double)samples.Count;

        // Error budget = allowed failure ratio over the window, e.g. target 99.95% => budget 0.05%.
        var errorBudgetTotal = 1 - targetRatio;
        var actualFailureRatio = 1 - currentRatio;
        var budgetConsumed = errorBudgetTotal <= 0 ? 1.0 : Math.Min(1.0, actualFailureRatio / errorBudgetTotal);
        var budgetRemainingPercent = Math.Max(0, (1 - budgetConsumed) * 100);

        var oldestSample = samples.Min(s => s.RecordedAtUtc);
        var newestSample = samples.Max(s => s.RecordedAtUtc);
        var elapsedHours = Math.Max((newestSample - oldestSample).TotalHours, 1.0 / 3600); // avoid divide-by-zero on a single sample
        var burnRatePerHour = budgetConsumed / elapsedHours;

        return new SloComplianceResult(
            CurrentValue: currentRatio,
            TargetValue: targetRatio,
            IsCompliant: currentRatio >= targetRatio,
            ErrorBudgetTotal: errorBudgetTotal,
            ErrorBudgetConsumed: budgetConsumed,
            ErrorBudgetRemainingPercent: budgetRemainingPercent,
            BurnRatePerHour: burnRatePerHour,
            SampleCount: samples.Count);
    }

    /// <summary>For latency SLOs (P95/P99): samples carry a millisecond Value.</summary>
    public static SloComplianceResult CalculateLatencyPercentile(IReadOnlyList<MetricSample> samples, double percentile, double targetMs)
    {
        if (samples.Count == 0)
        {
            return new SloComplianceResult(0, targetMs, false, 0, 0, 100, 0, 0);
        }

        var sorted = samples.Select(s => s.Value).OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1;
        index = Math.Clamp(index, 0, sorted.Count - 1);
        var observedValue = sorted[index];

        var withinTarget = samples.Count(s => s.Value <= targetMs);
        var complianceRatio = withinTarget / (double)samples.Count;

        return new SloComplianceResult(
            CurrentValue: observedValue,
            TargetValue: targetMs,
            IsCompliant: observedValue <= targetMs,
            ErrorBudgetTotal: 1.0,
            ErrorBudgetConsumed: 1 - complianceRatio,
            ErrorBudgetRemainingPercent: complianceRatio * 100,
            BurnRatePerHour: 0, // burn rate is defined for availability-style budgets; not meaningful per-sample for a percentile snapshot
            SampleCount: samples.Count);
    }
}
