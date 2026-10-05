using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.EventPlatform.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Atlas.IntegrationTests;

/// <summary>
/// Idempotent consumers proven against the real store, not a stub.
///
/// The mechanism is the unique index on
/// <c>(ConsumerGroup, EventId)</c> plus
/// <see cref="IdempotencyGuard.TryMarkProcessedAsync"/>. A unit test can only
/// show that the code takes the branch it takes; only a real PostgreSQL proves
/// that a redelivered event — including two consumers racing the same event —
/// runs the handler once. The CI pipeline applies the module migrations to the
/// `atlas_ci` database before this suite runs.
///
/// Every test uses its own consumer group, so the suite is safe to run in
/// parallel with itself and repeatable: a leftover row from a previous run can
/// never change the result.
/// </summary>
public sealed class EventIdempotencyIntegrationTests
{
    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? "Host=localhost;Port=5432;Database=atlas_ci;Username=atlas;Password=atlas_ci";

    private static EventPlatformDbContext Context() =>
        new(new DbContextOptionsBuilder<EventPlatformDbContext>()
            .UseNpgsql(ConnectionString)
            .Options);

    private static string Group() => $"integration-{Guid.NewGuid():N}";

    [Fact]
    public async Task A_redelivered_event_is_claimed_once_and_stays_claimed()
    {
        var group = Group();
        var eventId = Guid.NewGuid();

        await using (var first = Context())
        {
            var guard = new IdempotencyGuard(first);
            Assert.True(await guard.TryMarkProcessedAsync(group, eventId));
        }

        // A brand-new context (a restarted consumer, or another replica) must
        // see the same answer: the claim is in the database, not in memory.
        await using (var second = Context())
        {
            var guard = new IdempotencyGuard(second);
            Assert.False(await guard.TryMarkProcessedAsync(group, eventId));
            Assert.Equal(1, await second.IdempotencyRecords.CountAsync(r => r.ConsumerGroup == group && r.EventId == eventId));
        }
    }

    [Fact]
    public async Task A_released_claim_can_be_taken_again_after_a_handler_failure()
    {
        // The coordinator releases the claim when a handler throws so the retry
        // can re-dispatch the same event; a release that did not really delete
        // the row would turn every retry into a silent no-op.
        var group = Group();
        var eventId = Guid.NewGuid();

        await using (var scope = Context())
        {
            var guard = new IdempotencyGuard(scope);
            Assert.True(await guard.TryMarkProcessedAsync(group, eventId));
            await guard.ReleaseAsync(group, eventId);
        }

        await using (var retry = Context())
        {
            Assert.True(await new IdempotencyGuard(retry).TryMarkProcessedAsync(group, eventId));
        }
    }

    [Fact]
    public async Task Two_consumers_racing_the_same_event_produce_exactly_one_handler_run()
    {
        var group = Group();
        var eventId = Guid.NewGuid();

        // Two independent contexts, started together. Whichever insert loses
        // the unique-index race must report "already processed" instead of
        // throwing — that is precisely the at-least-once delivery case.
        await using var left = Context();
        await using var right = Context();

        var results = await Task.WhenAll(
            new IdempotencyGuard(left).TryMarkProcessedAsync(group, eventId),
            new IdempotencyGuard(right).TryMarkProcessedAsync(group, eventId));

        Assert.Equal(1, results.Count(claimed => claimed));
        await using var verify = Context();
        Assert.Equal(1, await verify.IdempotencyRecords.CountAsync(r => r.ConsumerGroup == group && r.EventId == eventId));
    }

    [Fact]
    public async Task A_claim_is_scoped_to_its_consumer_group()
    {
        // Two different consumers each need to see the event once: reusing a
        // group id would silently drop the second module's work.
        var eventId = Guid.NewGuid();
        var groupA = Group();
        var groupB = Group();

        await using var context = Context();
        Assert.True(await new IdempotencyGuard(context).TryMarkProcessedAsync(groupA, eventId));
        await using var other = Context();
        Assert.True(await new IdempotencyGuard(other).TryMarkProcessedAsync(groupB, eventId));
    }
}
