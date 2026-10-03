using Atlas.Modules.Observability.Application;
using Atlas.Modules.Observability.Domain;

namespace Atlas.Modules.DeploymentIntelligence.Application;

public sealed record CanaryAnalysisResult(
    bool AnalysisAvailable,
    bool CanaryPassed,
    string Recommendation,
    double BaselineErrorRate,
    double CanaryErrorRate,
    double BaselineP95Ms,
    double CanaryP95Ms,
    int BaselineSampleCount,
    int CanarySampleCount,
    IReadOnlyList<string> Evidence);

/// <summary>Pure, evidence-based canary comparison. It refuses to decide when samples are not version-attributed.</summary>
public static class CanaryAnalyzer
{
    public static CanaryAnalysisResult Analyze(
        IReadOnlyList<MetricSampleDto> samples,
        string canaryVersion,
        double errorRateMultiplier = 2,
        double p95IncreaseThresholdPercent = 20)
    {
        var versioned = samples.Where(s => !string.IsNullOrWhiteSpace(s.DeploymentVersion)).ToList();
        var canary = versioned.Where(s => string.Equals(s.DeploymentVersion, canaryVersion, StringComparison.OrdinalIgnoreCase)).ToList();
        var baseline = versioned.Where(s => !string.Equals(s.DeploymentVersion, canaryVersion, StringComparison.OrdinalIgnoreCase)).ToList();
        if (canary.Count == 0 || baseline.Count == 0)
            return Insufficient("Both baseline and canary version-attributed telemetry are required.");

        var canaryOutcomes = canary.Where(s => s.Success.HasValue).ToList();
        var baselineOutcomes = baseline.Where(s => s.Success.HasValue).ToList();
        var canaryLatency = canary.Where(s => !s.Success.HasValue && s.Value >= 0).Select(s => s.Value).ToList();
        var baselineLatency = baseline.Where(s => !s.Success.HasValue && s.Value >= 0).Select(s => s.Value).ToList();
        if (canaryOutcomes.Count == 0 || baselineOutcomes.Count == 0 || canaryLatency.Count == 0 || baselineLatency.Count == 0)
            return Insufficient("Canary and baseline require both outcome and latency samples.");

        var baselineError = baselineOutcomes.Count(s => s.Success == false) / (double)baselineOutcomes.Count;
        var canaryError = canaryOutcomes.Count(s => s.Success == false) / (double)canaryOutcomes.Count;
        var baselineP95 = Percentile(baselineLatency, 95);
        var canaryP95 = Percentile(canaryLatency, 95);
        var errorFailed = baselineError == 0 ? canaryError > 0 : canaryError > baselineError * errorRateMultiplier;
        var latencyFailed = baselineP95 > 0 && canaryP95 > baselineP95 * (1 + p95IncreaseThresholdPercent / 100);
        var evidence = new List<string>
        {
            $"Baseline {baseline.Count} samples, canary {canary.Count} samples.",
            $"Error rate baseline {baselineError:P2}, canary {canaryError:P2}.",
            $"P95 latency baseline {baselineP95:F1}ms, canary {canaryP95:F1}ms."
        };
        if (errorFailed) evidence.Add($"Canary error rate exceeded the configured {errorRateMultiplier:F1}x baseline threshold.");
        if (latencyFailed) evidence.Add($"Canary P95 exceeded the configured {p95IncreaseThresholdPercent:F1}% increase threshold.");
        var passed = !errorFailed && !latencyFailed;
        return new CanaryAnalysisResult(true, passed, passed ? "CANARY PASSED" : "CANARY FAILED — recommend rollback review", baselineError, canaryError, baselineP95, canaryP95, baseline.Count, canary.Count, evidence);
    }

    private static CanaryAnalysisResult Insufficient(string reason) => new(false, false, "Insufficient evidence.", 0, 0, 0, 0, 0, 0, new[] { reason });
    private static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var index = Math.Clamp((int)Math.Ceiling(percentile / 100 * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }
}
