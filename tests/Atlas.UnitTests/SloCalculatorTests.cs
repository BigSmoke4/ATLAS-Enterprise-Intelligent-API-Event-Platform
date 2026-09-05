using Atlas.Modules.Observability.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class SloCalculatorTests
{
    private static MetricSample Outcome(bool success, DateTimeOffset at) =>
        MetricSample.CreateOutcome(Guid.NewGuid(), Guid.NewGuid(), SloMetricType.Availability, success, at);

    private static MetricSample Latency(double ms, DateTimeOffset at) =>
        MetricSample.CreateLatency(Guid.NewGuid(), Guid.NewGuid(), SloMetricType.LatencyP95, ms, at);

    [Fact]
    public void No_samples_is_reported_as_non_compliant_not_100_percent()
    {
        var result = SloCalculator.CalculateAvailability(new List<MetricSample>(), targetRatio: 0.999, TimeSpan.FromHours(1));
        Assert.False(result.IsCompliant);
        Assert.Equal(0, result.SampleCount);
    }

    [Fact]
    public void Meets_target_when_success_ratio_at_or_above_target()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = Enumerable.Range(0, 1000).Select(i => Outcome(i < 999, now.AddSeconds(i))).ToList(); // 99.9%

        var result = SloCalculator.CalculateAvailability(samples, targetRatio: 0.999, TimeSpan.FromHours(1));

        Assert.True(result.IsCompliant);
        Assert.Equal(0.999, result.CurrentValue, precision: 3);
    }

    [Fact]
    public void Error_budget_fully_consumed_when_failure_ratio_equals_budget()
    {
        var now = DateTimeOffset.UtcNow;
        // Target 99% => budget 1%. 990 success / 10 failure out of 1000 = exactly at budget.
        var samples = Enumerable.Range(0, 1000).Select(i => Outcome(i < 990, now.AddSeconds(i))).ToList();

        var result = SloCalculator.CalculateAvailability(samples, targetRatio: 0.99, TimeSpan.FromHours(1));

        Assert.Equal(1.0, result.ErrorBudgetConsumed, precision: 2);
        Assert.True(result.ErrorBudgetRemainingPercent < 1);
    }

    [Fact]
    public void Exceeding_error_budget_marks_non_compliant_and_over_100_percent_consumed_is_capped()
    {
        var now = DateTimeOffset.UtcNow;
        // Target 99.9% => budget 0.1%. 90% failure rate blows way past budget.
        var samples = Enumerable.Range(0, 100).Select(i => Outcome(i < 10, now.AddSeconds(i))).ToList();

        var result = SloCalculator.CalculateAvailability(samples, targetRatio: 0.999, TimeSpan.FromHours(1));

        Assert.False(result.IsCompliant);
        Assert.Equal(1.0, result.ErrorBudgetConsumed); // capped at 100%, never reports >100% consumed
        Assert.Equal(0, result.ErrorBudgetRemainingPercent);
    }

    [Fact]
    public void Latency_p95_is_compliant_when_95th_percentile_under_target()
    {
        var now = DateTimeOffset.UtcNow;
        // 100 samples: 95 at 50ms, 5 at 500ms -> p95 should be at/near 50ms.
        var samples = Enumerable.Range(0, 95).Select(i => Latency(50, now.AddSeconds(i)))
            .Concat(Enumerable.Range(0, 5).Select(i => Latency(500, now.AddSeconds(95 + i))))
            .ToList();

        var result = SloCalculator.CalculateLatencyPercentile(samples, percentile: 95, targetMs: 100);

        Assert.True(result.IsCompliant);
        Assert.True(result.CurrentValue <= 100);
    }

    [Fact]
    public void Latency_p95_is_non_compliant_when_95th_percentile_exceeds_target()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = Enumerable.Range(0, 100).Select(i => Latency(200, now.AddSeconds(i))).ToList();

        var result = SloCalculator.CalculateLatencyPercentile(samples, percentile: 95, targetMs: 100);

        Assert.False(result.IsCompliant);
        Assert.Equal(200, result.CurrentValue);
    }
}
