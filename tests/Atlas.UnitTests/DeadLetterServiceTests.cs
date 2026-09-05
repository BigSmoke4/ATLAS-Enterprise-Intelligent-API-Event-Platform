using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.EventPlatform.Infrastructure;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Atlas.UnitTests;

file class FakePublisher : IEventPublisher
{
    public int PublishCount { get; private set; }
    public Task PublishAsync<TEvent>(string topic, TEvent @event, CancellationToken ct = default) where TEvent : IIntegrationEvent
    {
        PublishCount++;
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
