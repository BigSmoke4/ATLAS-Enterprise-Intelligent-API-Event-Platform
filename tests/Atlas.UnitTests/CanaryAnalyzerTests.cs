using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.Observability.Application;
using Xunit;

namespace Atlas.UnitTests;

public sealed class CanaryAnalyzerTests
{
    [Fact]
    public void Refuses_to_decide_without_version_attributed_telemetry()
    {
        var result = CanaryAnalyzer.Analyze(new[] { new MetricSampleDto(1, true, DateTimeOffset.UtcNow) }, "v2");
        Assert.False(result.AnalysisAvailable);
        Assert.Equal("Insufficient evidence.", result.Recommendation);
    }

    [Fact]
    public void Fails_canary_when_error_rate_exceeds_configured_multiplier()
    {
        var now = DateTimeOffset.UtcNow;
        // Mirrors the production caller (DeploymentRegressionService), which
        // concatenates outcome samples (Success set) with latency samples
        // (Success null, Value = milliseconds).
        var samples = new List<MetricSampleDto>
        {
            new(0, true, now, "v1"), new(0, true, now, "v1"), new(0, true, now, "v1"),
            new(0, true, now, "v1"), new(0, false, now, "v2"), new(0, true, now, "v2"),
            new(95, null, now, "v1"), new(98, null, now, "v1"),
            new(97, null, now, "v2"), new(99, null, now, "v2")
        };
        var result = CanaryAnalyzer.Analyze(samples, "v2");
        Assert.True(result.AnalysisAvailable);
        Assert.False(result.CanaryPassed);
        Assert.Contains("FAILED", result.Recommendation);
    }
}
