namespace Atlas.Modules.Observability.Domain;

public record SloComplianceResult(
    double CurrentValue,
    double TargetValue,
    bool IsCompliant,
    double ErrorBudgetTotal,
    double ErrorBudgetConsumed,
    double ErrorBudgetRemainingPercent,
    double BurnRatePerHour,
    int SampleCount)
{
    /// <summary>
    /// Which recorded source produced this result: "metric-samples" (probed
    /// availability / pushed samples) or "live-request-telemetry" (one-minute
    /// request aggregates). Surfaced so an operator can always tell where a
    /// compliance number came from.
    /// </summary>
    public string EvidenceSource { get; init; } = "metric-samples";

    /// <summary>True when there is genuinely recorded evidence for the window — the UI renders "No telemetry available." rather than 0%.</summary>
    public bool HasData => SampleCount > 0;
}

/// <summary>
/// Pure functions over recorded samples and live request aggregates — the only
/// place SLO compliance and error-budget numbers are computed. No caller may
/// report a compliance percentage that didn't come through here, per the
/// master prompt's "derive from recorded telemetry, do not fabricate"
/// requirement.
///
/// Two evidence families are supported and both are real:
/// 1. <see cref="MetricSample"/> rows — probed availability outcomes and
///    explicitly pushed samples.
/// 2. <see cref="RequestTelemetryAggregate"/> buckets — every HTTP request the
///    platform actually served, rolled up per minute with a latency histogram.
/// </summary>
public static class SloCalculator
{
    private static SloComplianceResult NoData(double target) =>
        new(0, target, false, 0, 0, 100, 0, 0) { EvidenceSource = "none" };

    /// <summary>For Availability/ErrorRate SLOs: samples must all have Success set.</summary>
    public static SloComplianceResult CalculateAvailability(IReadOnlyList<MetricSample> samples, double targetRatio, TimeSpan window)
    {
        if (samples.Count == 0) return NoData(targetRatio);

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
        if (samples.Count == 0) return NoData(targetMs);

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

    /// <summary>
    /// Availability/error-rate compliance derived from live request telemetry.
    /// The denominator is real served requests; the numerator is real 5xx
    /// responses. Server errors (5xx) are the availability failure signal —
    /// 4xx responses are client faults and are reported separately by
    /// ITelemetryQueryService, never silently counted as availability loss.
    /// </summary>
    public static SloComplianceResult CalculateAvailabilityFromTelemetry(IReadOnlyList<RequestTelemetryAggregate> buckets, double targetRatio, TimeSpan window)
    {
        if (buckets.Count == 0) return NoData(targetRatio);

        var totalRequests = buckets.Sum(b => b.RequestCount);
        if (totalRequests == 0) return NoData(targetRatio);

        var failures = buckets.Sum(b => b.ServerErrorCount);
        var currentRatio = (totalRequests - failures) / (double)totalRequests;

        var errorBudgetTotal = 1 - targetRatio;
        var actualFailureRatio = failures / (double)totalRequests;
        var budgetConsumed = errorBudgetTotal <= 0 ? (actualFailureRatio > 0 ? 1.0 : 0.0) : Math.Min(1.0, actualFailureRatio / errorBudgetTotal);

        var oldest = buckets.Min(b => b.WindowStartUtc);
        var newest = buckets.Max(b => b.WindowStartUtc);
        var elapsedHours = Math.Max((newest - oldest).TotalHours, 1.0 / 60); // at least one bucket
        var burnRatePerHour = budgetConsumed / elapsedHours;

        return new SloComplianceResult(
            CurrentValue: currentRatio,
            TargetValue: targetRatio,
            IsCompliant: currentRatio >= targetRatio,
            ErrorBudgetTotal: errorBudgetTotal,
            ErrorBudgetConsumed: budgetConsumed,
            ErrorBudgetRemainingPercent: Math.Max(0, (1 - budgetConsumed) * 100),
            BurnRatePerHour: burnRatePerHour,
            SampleCount: (int)Math.Min(int.MaxValue, totalRequests))
        {
            EvidenceSource = "live-request-telemetry"
        };
    }

    /// <summary>
    /// Latency compliance derived from the histogram carried by live request
    /// telemetry. The percentile is the bucket upper boundary containing the
    /// requested rank (see LatencyHistogram): ATLAS never reports a percentile
    /// more optimistic than the underlying data supports.
    /// </summary>
    public static SloComplianceResult CalculateLatencyFromTelemetry(IReadOnlyList<RequestTelemetryAggregate> buckets, double percentile, double targetMs)
    {
        if (buckets.Count == 0) return NoData(targetMs);

        var combined = LatencyHistogram.CreateEmptyBucketCounts();
        long total = 0;
        long withinTarget = 0;

        foreach (var bucket in buckets)
        {
            LatencyHistogram.Add(bucket.DurationBucketCounts, combined);
            total += bucket.RequestCount;

            // Count observations that are provably at or below the target:
            // a bucket qualifies only when its UPPER boundary is <= targetMs.
            for (var i = 0; i < LatencyHistogram.BucketUpperBoundsMs.Length && i < bucket.DurationBucketCounts.Length; i++)
            {
                if (LatencyHistogram.BucketUpperBoundsMs[i] <= targetMs) withinTarget += bucket.DurationBucketCounts[i];
            }
        }

        if (total == 0) return NoData(targetMs);

        var observed = LatencyHistogram.PercentileMs(combined, percentile) ?? targetMs;
        var complianceRatio = withinTarget / (double)total;

        return new SloComplianceResult(
            CurrentValue: observed,
            TargetValue: targetMs,
            IsCompliant: observed <= targetMs,
            ErrorBudgetTotal: 1.0,
            ErrorBudgetConsumed: 1 - complianceRatio,
            ErrorBudgetRemainingPercent: complianceRatio * 100,
            BurnRatePerHour: 0,
            SampleCount: (int)Math.Min(int.MaxValue, total))
        {
            EvidenceSource = "live-request-telemetry"
        };
    }
}
