using Atlas.Modules.DeploymentIntelligence.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class RegressionAnalyzerTests
{
    [Fact]
    public void No_regression_when_metrics_are_stable()
    {
        var result = RegressionAnalyzer.Analyze(beforeErrorRate: 0.01, afterErrorRate: 0.011, beforeP95Ms: 100, afterP95Ms: 105);
        Assert.False(result.RegressionDetected);
    }

    [Fact]
    public void Detects_regression_from_error_rate_spike_matching_master_prompt_example()
    {
        // Mirrors the master prompt's example: error rate +4.2% after a deployment.
        var result = RegressionAnalyzer.Analyze(beforeErrorRate: 0.01, afterErrorRate: 0.0142, beforeP95Ms: 80, afterP95Ms: 82);
        Assert.True(result.RegressionDetected);
        Assert.Contains("error rate increased", result.Summary);
    }

    [Fact]
    public void Detects_regression_from_latency_spike()
    {
        var result = RegressionAnalyzer.Analyze(beforeErrorRate: 0.01, afterErrorRate: 0.01, beforeP95Ms: 100, afterP95Ms: 250);
        Assert.True(result.RegressionDetected);
        Assert.Contains("P95 latency increased", result.Summary);
    }

    [Fact]
    public void Zero_baseline_error_rate_with_new_errors_is_flagged_not_divide_by_zero()
    {
        var result = RegressionAnalyzer.Analyze(beforeErrorRate: 0, afterErrorRate: 0.02, beforeP95Ms: 100, afterP95Ms: 100);
        Assert.True(result.RegressionDetected);
        Assert.Equal(100, result.ErrorRateDeltaPercent);
    }

    [Fact]
    public void Improvement_is_not_flagged_as_regression()
    {
        var result = RegressionAnalyzer.Analyze(beforeErrorRate: 0.05, afterErrorRate: 0.01, beforeP95Ms: 300, afterP95Ms: 100);
        Assert.False(result.RegressionDetected);
    }
}
