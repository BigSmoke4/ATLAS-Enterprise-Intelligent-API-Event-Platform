using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.EventPlatform.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Atlas.UnitTests;

file class FakeAudit : IAuditSink
{
    public List<AuditRecord> Records { get; } = new();
    public Task RecordAsync(AuditRecord record, CancellationToken ct = default) { Records.Add(record); return Task.CompletedTask; }
}

file class FakePublisher : IEventPublisher, IRawEventPublisher
{
    public int PublishCount { get; private set; }
    public string? RawPayload { get; private set; }
    public Task PublishAsync<TEvent>(string topic, TEvent @event, CancellationToken ct = default) where TEvent : IIntegrationEvent
    {
        PublishCount++;
        return Task.CompletedTask;
    }
    public Task PublishRawAsync(string topic, string payloadJson, string eventType, int version, Guid eventId, Guid correlationId, Guid? causationId, string producer, DateTimeOffset timestampUtc, CancellationToken ct = default)
    {
        PublishCount++;
        RawPayload = payloadJson;
        return Task.CompletedTask;
    }
}

public class DeadLetterServiceTests
{
    private static EventPlatformDbContext NewDb() =>
        new(new DbContextOptionsBuilder<EventPlatformDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task Repeated_failures_for_the_same_event_increment_retry_count_not_duplicate_rows()
    {
        using var db = NewDb();
        var service = new DeadLetterService(db, publisher: null);
        var eventId = Guid.NewGuid();

        await service.RouteToDeadLetterAsync("orders", eventId, "OrderCreated", Guid.NewGuid(), "{}", "boom", CancellationToken.None);
        await service.RouteToDeadLetterAsync("orders", eventId, "OrderCreated", Guid.NewGuid(), "{}", "boom again", CancellationToken.None);

        var entries = await service.ListAsync(null);
        var entry = Assert.Single(entries);
        Assert.Equal(2, entry.RetryCount);
        Assert.Equal("boom again", entry.FailureReason);
    }

    [Fact]
    public async Task List_filters_by_topic_event_type_and_event_id()
    {
        using var db = NewDb();
        var service = new DeadLetterService(db, publisher: null);
        var matchingId = Guid.NewGuid();
        await service.RouteToDeadLetterAsync("orders", matchingId, "OrderCreated", Guid.NewGuid(), "{\"order\":1}", "boom", CancellationToken.None);
        await service.RouteToDeadLetterAsync("payments", Guid.NewGuid(), "PaymentFailed", Guid.NewGuid(), "{\"payment\":1}", "boom", CancellationToken.None);

        var result = await service.ListAsync("orders", eventType: "OrderCreated", eventId: matchingId);

        var entry = Assert.Single(result);
        Assert.Equal(matchingId, entry.EventId);
        Assert.Equal("{\"order\":1}", entry.PayloadJson);
    }

    [Fact]
    public async Task Replay_uses_raw_publisher_and_preserves_original_payload()
    {
        using var db = NewDb();
        var publisher = new FakePublisher();
        var service = new DeadLetterService(db, publisher);
        const string payload = "{\"original\":true,\"nested\":{\"value\":7}}";
        await service.RouteToDeadLetterAsync("orders", Guid.NewGuid(), "OrderCreated", Guid.NewGuid(), payload, "boom", CancellationToken.None);
        var entry = (await service.ListAsync(null)).Single();

        var result = await service.ReplayAsync(entry.Id, dryRun: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(payload, publisher.RawPayload);
        Assert.Equal(1, publisher.PublishCount);
    }

    [Fact]
    public async Task Replay_fails_when_no_publisher_is_configured()
    {
        using var db = NewDb();
        var service = new DeadLetterService(db, publisher: null);
        await service.RouteToDeadLetterAsync("orders", Guid.NewGuid(), "OrderCreated", Guid.NewGuid(), "{}", "boom", CancellationToken.None);
        var entry = (await service.ListAsync(null)).Single();

        var result = await service.ReplayAsync(entry.Id, dryRun: false, CancellationToken.None);

        Assert.False(result.Success);
    }

    [Fact]
    public async Task Dry_run_never_publishes_or_marks_replayed()
    {
        using var db = NewDb();
        var publisher = new FakePublisher();
        var service = new DeadLetterService(db, publisher);
        await service.RouteToDeadLetterAsync("orders", Guid.NewGuid(), "OrderCreated", Guid.NewGuid(), "{}", "boom", CancellationToken.None);
        var entry = (await service.ListAsync(null)).Single();

        var result = await service.ReplayAsync(entry.Id, dryRun: true, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(0, publisher.PublishCount);
        Assert.Single(await service.ListAsync(null)); // still not replayed, still listed
    }

    [Fact]
    public async Task Live_replay_publishes_and_marks_replayed_and_disappears_from_active_list()
    {
        using var db = NewDb();
        var publisher = new FakePublisher();
        var service = new DeadLetterService(db, publisher);
        await service.RouteToDeadLetterAsync("orders", Guid.NewGuid(), "OrderCreated", Guid.NewGuid(), "{}", "boom", CancellationToken.None);
        var entry = (await service.ListAsync(null)).Single();

        var result = await service.ReplayAsync(entry.Id, dryRun: false, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(1, publisher.PublishCount);
        Assert.Empty(await service.ListAsync(null)); // ListAsync filters out replayed entries
    }

    [Fact]
    public async Task Replay_and_manual_mark_actions_are_audited_with_outcomes()
    {
        using var db = NewDb();
        var publisher = new FakePublisher();
        var audit = new FakeAudit();
        var service = new DeadLetterService(db, publisher, audit);
        await service.RouteToDeadLetterAsync("orders", Guid.NewGuid(), "OrderCreated", Guid.NewGuid(), "{}", "boom", CancellationToken.None);
        var first = (await service.ListAsync(null)).Single();
        Assert.True((await service.ReplayAsync(first.Id, dryRun: true, CancellationToken.None)).Success);
        Assert.True((await service.ReplayAsync(first.Id, dryRun: false, CancellationToken.None)).Success);

        await service.RouteToDeadLetterAsync("orders", Guid.NewGuid(), "OrderCreated", Guid.NewGuid(), "{}", "boom", CancellationToken.None);
        var second = (await service.ListAsync(null)).Single();
        Assert.True((await service.MarkReplayedAsync(second.Id)).Success);

        Assert.Equal(new[] { "dead_letter.replay_dry_run", "dead_letter.replay", "dead_letter.mark_replayed" }, audit.Records.Select(x => x.Action));
        Assert.Contains("published", audit.Records[1].AfterJson);
    }

    [Fact]
    public async Task Cannot_replay_the_same_event_twice()
    {
        using var db = NewDb();
        var publisher = new FakePublisher();
        var service = new DeadLetterService(db, publisher);
        await service.RouteToDeadLetterAsync("orders", Guid.NewGuid(), "OrderCreated", Guid.NewGuid(), "{}", "boom", CancellationToken.None);
        var entryId = (await service.ListAsync(null)).Single().Id;

        await service.ReplayAsync(entryId, dryRun: false, CancellationToken.None);
        var second = await service.ReplayAsync(entryId, dryRun: false, CancellationToken.None);

        Assert.False(second.Success);
        Assert.Equal(1, publisher.PublishCount);
    }
}
