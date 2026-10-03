using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.EventPlatform.Domain;
using Atlas.Shared.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Atlas.UnitTests;

public sealed class EventProcessingCoordinatorTests
{
    private sealed class Guard : IIdempotencyGuard
    {
        public int Claims { get; private set; }
        public int Releases { get; private set; }
        public Task<bool> TryMarkProcessedAsync(string group, Guid id, CancellationToken ct = default) { Claims++; return Task.FromResult(true); }
        public Task ReleaseAsync(string group, Guid id, CancellationToken ct = default) { Releases++; return Task.CompletedTask; }
    }

    private sealed class Dispatcher : IEventHandlerDispatcher
    {
        private readonly bool _failFirst;
        public int Calls { get; private set; }
        public Dispatcher(bool failFirst) => _failFirst = failFirst;
        public Task<bool> DispatchAsync(string eventType, string payloadJson, CancellationToken ct = default)
        { Calls++; if (_failFirst && Calls == 1) throw new InvalidOperationException("transient"); return Task.FromResult(true); }
    }

    private sealed class DeadLetters : IDeadLetterService
    {
        public int Calls { get; private set; }
        public Task RouteToDeadLetterAsync(string topic, Guid eventId, string eventType, Guid correlationId, string payloadJson, string failureReason, CancellationToken ct = default, int version = 1) { Calls++; return Task.CompletedTask; }
        public Task<IReadOnlyList<DeadLetterEvent>> ListAsync(string? topic, int page = 1, int pageSize = 50, CancellationToken ct = default, string? eventType = null, DateTimeOffset? fromUtc = null, DateTimeOffset? toUtc = null, Guid? eventId = null) => Task.FromResult<IReadOnlyList<DeadLetterEvent>>(Array.Empty<DeadLetterEvent>());
        public Task<DeadLetterOperationResult> MarkReplayedAsync(Guid id, CancellationToken ct = default) => Task.FromResult(DeadLetterOperationResult.Ok());
        public Task<DeadLetterOperationResult> ReplayAsync(Guid id, bool dryRun, CancellationToken ct = default) => Task.FromResult(DeadLetterOperationResult.Ok());
    }

    private static string Payload(Guid eventId) => $"{{\"EventId\":\"{eventId}\",\"EventType\":\"OrderCreated\",\"Version\":1,\"TimestampUtc\":\"{DateTimeOffset.UtcNow:O}\",\"CorrelationId\":\"{Guid.NewGuid()}\",\"Producer\":\"tests\"}}";

    [Fact]
    public async Task Failed_handler_releases_claim_and_retries_successfully()
    {
        var guard = new Guard(); var dispatcher = new Dispatcher(failFirst: true); var dlq = new DeadLetters();
        var coordinator = new EventProcessingCoordinator(guard, dispatcher, dlq, NullLogger<EventProcessingCoordinator>.Instance);
        await coordinator.ProcessAsync("orders", "OrderCreated", 1, Guid.NewGuid(), Payload(Guid.NewGuid()), "tests", 1, TimeSpan.Zero);

        Assert.Equal(2, dispatcher.Calls);
        Assert.Equal(1, guard.Releases);
        Assert.Equal(0, dlq.Calls);
    }

    [Fact]
    public async Task Exhausted_failures_are_sent_to_the_dead_letter_service()
    {
        var guard = new Guard(); var dispatcher = new Dispatcher(failFirst: true); var dlq = new DeadLetters();
        // One retry means the dispatcher fails on the first call but succeeds on the second in this fixture;
        // invalid JSON exercises the exhaustion path deterministically.
        var coordinator = new EventProcessingCoordinator(guard, dispatcher, dlq, NullLogger<EventProcessingCoordinator>.Instance);
        await coordinator.ProcessAsync("orders", "OrderCreated", 1, Guid.NewGuid(), "not-json", "tests", 0, TimeSpan.Zero);
        Assert.Equal(1, dlq.Calls);
    }
}
