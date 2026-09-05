using Atlas.Modules.Reliability.Domain;
using Xunit;

namespace Atlas.UnitTests;

public class SlidingWindowCounterTests
{
    [Fact]
    public void Allows_up_to_limit_within_window()
    {
        var counter = new SlidingWindowCounter(limit: 3, window: TimeSpan.FromSeconds(10));
        var now = DateTimeOffset.UtcNow;

        Assert.True(counter.TryHit(now));
        Assert.True(counter.TryHit(now.AddSeconds(1)));
        Assert.True(counter.TryHit(now.AddSeconds(2)));
        Assert.False(counter.TryHit(now.AddSeconds(3)));
    }

    [Fact]
    public void Expired_events_free_up_capacity()
    {
        var counter = new SlidingWindowCounter(limit: 2, window: TimeSpan.FromSeconds(5));
        var now = DateTimeOffset.UtcNow;

        Assert.True(counter.TryHit(now));
        Assert.True(counter.TryHit(now.AddSeconds(1)));
        Assert.False(counter.TryHit(now.AddSeconds(2)));

        Assert.True(counter.TryHit(now.AddSeconds(6))); // first event now outside window
    }
}
