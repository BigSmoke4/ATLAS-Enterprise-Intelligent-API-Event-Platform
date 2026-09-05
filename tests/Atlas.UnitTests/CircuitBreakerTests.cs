using Atlas.Modules.Reliability.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class CircuitBreakerTests
{
    [Fact]
    public void Opens_after_failure_threshold_exceeded_with_minimum_volume()
    {
        var cb = new CircuitBreaker("payment-api", failureThreshold: 0.5, minimumRequestVolume: 4,
            samplingDuration: TimeSpan.FromMinutes(1), openDuration: TimeSpan.FromSeconds(30));

        for (int i = 0; i < 2; i++) { cb.TryAcquire(); cb.RecordResult(true); }
        for (int i = 0; i < 2; i++) { cb.TryAcquire(); cb.RecordResult(false); }

        Assert.Equal(CircuitState.Open, cb.State);
        Assert.False(cb.TryAcquire());
    }

    [Fact]
    public void Stays_closed_below_minimum_request_volume_even_if_all_fail()
    {
        var cb = new CircuitBreaker("orders-api", failureThreshold: 0.1, minimumRequestVolume: 10);
        cb.TryAcquire(); cb.RecordResult(false);
        cb.TryAcquire(); cb.RecordResult(false);

        Assert.Equal(CircuitState.Closed, cb.State);
    }

    [Fact]
    public void HalfOpen_trial_success_closes_circuit()
    {
        var cb = new CircuitBreaker("inventory-api", failureThreshold: 0.5, minimumRequestVolume: 2,
            openDuration: TimeSpan.FromMilliseconds(1));
        cb.TryAcquire(); cb.RecordResult(false);
        cb.TryAcquire(); cb.RecordResult(false);
        Assert.Equal(CircuitState.Open, cb.State);

        Thread.Sleep(5);
        Assert.True(cb.TryAcquire()); // trial request allowed
        cb.RecordResult(true);

        Assert.Equal(CircuitState.Closed, cb.State);
    }

    [Fact]
    public void HalfOpen_trial_failure_reopens_circuit()
    {
        var cb = new CircuitBreaker("billing-api", failureThreshold: 0.5, minimumRequestVolume: 2,
            openDuration: TimeSpan.FromMilliseconds(1));
        cb.TryAcquire(); cb.RecordResult(false);
        cb.TryAcquire(); cb.RecordResult(false);

        Thread.Sleep(5);
        Assert.True(cb.TryAcquire());
        cb.RecordResult(false);

        Assert.Equal(CircuitState.Open, cb.State);
    }
}
