using Atlas.Modules.DeploymentIntelligence.Domain;
using Atlas.Modules.DeploymentIntelligence.Infrastructure;
using Atlas.Shared.Contracts;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The outbox contract, without a database: a row carries a complete, validated
/// envelope; it is pending exactly once; failures back off exponentially and
/// stop at the attempt limit instead of retrying forever.
/// </summary>
public class OutboxMessageTests
{
    private sealed record TestEvent(Guid EventId, string EventType, int Version, DateTimeOffset TimestampUtc,
        Guid CorrelationId, Guid? CausationId, string Producer, string OrderId) : IIntegrationEvent;

    private static TestEvent Event() => new(
        Guid.NewGuid(), "DeploymentRecorded", 1, DateTimeOffset.UtcNow, Guid.NewGuid(), null, "tests", "order-1");

    [Fact]
    public void A_row_carries_the_full_envelope_and_is_pending_immediately()
    {
        var @event = Event();
        var row = OutboxMessage.Create("atlas.events.deployments", @event);

        Assert.Equal(@event.EventId, row.EventId);
        Assert.Equal(@event.CorrelationId, row.CorrelationId);
        Assert.Equal(@event.EventType, row.EventType);
        Assert.Equal(1, row.Version);
        Assert.Equal("tests", row.Producer);
        Assert.Contains(@event.EventId.ToString(), row.PayloadJson);
        Assert.True(row.IsPending(DateTimeOffset.UtcNow));
        Assert.False(row.IsAbandoned);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public void A_sent_row_is_no_longer_pending()
    {
        var row = OutboxMessage.Create("t", Event());
        row.MarkSent(DateTimeOffset.UtcNow);

        Assert.False(row.IsPending(DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public void An_incomplete_envelope_is_rejected_at_write_time()
    {
        // EventId is empty and Producer is blank — the validator refuses it, so
        // the row never reaches the table for a relay to discover hours later.
        var invalid = new TestEvent(Guid.Empty, "", 0, DateTimeOffset.UtcNow, Guid.Empty, null, "", "order-1");
        Assert.ThrowsAny<ArgumentException>(() => OutboxMessage.Create("t", invalid));
    }

    [Fact]
    public void A_failed_attempt_backs_off_and_keeps_the_error()
    {
        var row = OutboxMessage.Create("t", Event());
        var now = DateTimeOffset.UtcNow;
        row.RecordFailure("broker unreachable", TimeSpan.FromSeconds(30), maxAttempts: 5, now);

        Assert.Equal(1, row.Attempts);
        Assert.Equal("broker unreachable", row.LastError);
        Assert.False(row.IsPending(now));                                  // not before the delay elapses
        Assert.True(row.IsPending(now.AddSeconds(31)));
        Assert.False(row.IsAbandoned);
    }

    [Fact]
    public void The_final_attempt_abandons_the_row_instead_of_retrying_forever()
    {
        var row = OutboxMessage.Create("t", Event());
        row.RecordFailure("first", TimeSpan.FromSeconds(1), maxAttempts: 2, DateTimeOffset.UtcNow);
        row.RecordFailure("second", TimeSpan.FromSeconds(1), maxAttempts: 2, DateTimeOffset.UtcNow);

        Assert.True(row.IsAbandoned);
        Assert.False(row.IsPending(DateTimeOffset.UtcNow.AddDays(1)));
    }

    [Fact]
    public void A_long_error_is_truncated_to_the_column_width()
    {
        var row = OutboxMessage.Create("t", Event());
        row.RecordFailure(new string('x', 5000), TimeSpan.FromSeconds(1), maxAttempts: 5, DateTimeOffset.UtcNow);

        Assert.NotNull(row.LastError);
        Assert.Equal(2000, row.LastError!.Length);
    }

    [Theory]
    [InlineData(1, 5.0)]      // initial backoff
    [InlineData(2, 10.0)]
    [InlineData(3, 20.0)]
    [InlineData(20, 600.0)]   // capped at MaxBackoff (10 minutes)
    public void Backoff_doubles_and_is_capped(int attempt, double expectedSeconds)
    {
        var backoff = OutboxRelayService.BackoffFor(attempt, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(10));
        Assert.Equal(expectedSeconds, backoff.TotalSeconds, precision: 3);
    }
}
