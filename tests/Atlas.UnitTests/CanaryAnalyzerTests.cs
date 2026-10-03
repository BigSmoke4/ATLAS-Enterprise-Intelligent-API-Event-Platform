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
        var samples = new List<MetricSampleDto>
        {
            new(10, true, now, "v1"), new(20, true, now, "v1"), new(100, true, now, "v1"),
            new(110, true, now, "v1"), new(12, false, now, "v2"), new(120, true, now, "v2")
        };
        var result = CanaryAnalyzer.Analyze(samples, "v2");
        Assert.True(result.AnalysisAvailable);
        Assert.False(result.CanaryPassed);
        Assert.Contains("FAILED", result.Recommendation);
    }
}
