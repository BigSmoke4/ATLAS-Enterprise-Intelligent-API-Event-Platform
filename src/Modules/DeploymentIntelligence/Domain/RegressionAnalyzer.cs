namespace Atlas.Modules.DeploymentIntelligence.Domain;

public record RegressionAnalysisResult(
    bool RegressionDetected,
    double BeforeErrorRate,
    double AfterErrorRate,
    double ErrorRateDeltaPercent,
    double BeforeP95Ms,
    double AfterP95Ms,
    double LatencyDeltaPercent,
    string Summary);

/// <summary>
/// Compares a window of telemetry immediately before and after a deployment
/// timestamp and flags a likely regression. Both windows must be supplied by
/// the caller from real Observability.MetricSample data — this class does
/// no fabrication, only comparison math, per the master prompt's
/// "must be calculated from actual stored telemetry" requirement.
/// </summary>
public static class RegressionAnalyzer
{
    /// <param name="errorRateRegressionThresholdPercent">e.g. 20 means "flag if error rate increased by 20% relative to before".</param>
    /// <param name="latencyRegressionThresholdPercent">e.g. 20 means "flag if P95 latency increased by 20% relative to before".</param>
    public static RegressionAnalysisResult Analyze(
        double beforeErrorRate, double afterErrorRate,
        double beforeP95Ms, double afterP95Ms,
        double errorRateRegressionThresholdPercent = 20,
        double latencyRegressionThresholdPercent = 20)
    {
        var errorDeltaPercent = PercentChange(beforeErrorRate, afterErrorRate);
        var latencyDeltaPercent = PercentChange(beforeP95Ms, afterP95Ms);

        var errorRegression = errorDeltaPercent >= errorRateRegressionThresholdPercent;
        var latencyRegression = latencyDeltaPercent >= latencyRegressionThresholdPercent;
        var regressionDetected = errorRegression || latencyRegression;

        var summary = regressionDetected
            ? BuildRegressionSummary(errorRegression, errorDeltaPercent, latencyRegression, latencyDeltaPercent)
            : "No regression detected within configured thresholds.";

        return new RegressionAnalysisResult(regressionDetected, beforeErrorRate, afterErrorRate, errorDeltaPercent,
            beforeP95Ms, afterP95Ms, latencyDeltaPercent, summary);
    }

    private static double PercentChange(double before, double after)
    {
        if (before <= 0) return after > 0 ? 100 : 0; // avoid divide-by-zero; any increase from zero baseline is reported as +100%
        return (after - before) / before * 100.0;
    }

    private static string BuildRegressionSummary(bool errorRegression, double errorDeltaPercent, bool latencyRegression, double latencyDeltaPercent)
    {
        var parts = new List<string>();
        if (errorRegression) parts.Add($"error rate increased {errorDeltaPercent:F1}%");
        if (latencyRegression) parts.Add($"P95 latency increased {latencyDeltaPercent:F1}%");
        return "Potential deployment regression: " + string.Join(" and ", parts) + ".";
    }
}
