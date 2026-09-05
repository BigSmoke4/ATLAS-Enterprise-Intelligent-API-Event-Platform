using Atlas.Modules.EventPlatform.Domain;
using Atlas.Modules.EventPlatform.Infrastructure;
using Atlas.Shared.Application;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.EventPlatform.Application;

public interface IDeadLetterService
{
    Task RouteToDeadLetterAsync(string topic, Guid eventId, string eventType, Guid correlationId, string payloadJson, string failureReason, CancellationToken ct = default);
    Task<IReadOnlyList<DeadLetterEvent>> ListAsync(string? topic, int page = 1, int pageSize = 50, CancellationToken ct = default);

    /// <summary>Flags a dead-lettered event as replayed WITHOUT re-publishing it — use when the fix was applied out-of-band (e.g. a manual data correction) and the message itself should not run again.</summary>
    Task<DeadLetterOperationResult> MarkReplayedAsync(Guid deadLetterEventId, CancellationToken ct = default);

    /// <summary>
    /// Real replay: re-publishes the dead-lettered message's original
    /// payload back onto its original topic via IEventPublisher, then marks
    /// it replayed only if the publish succeeds. Requires Kafka to be
    /// configured (IEventPublisher registered) — fails clearly if not,
    /// rather than silently marking replayed without actually resending.
    /// </summary>
    Task<DeadLetterOperationResult> ReplayAsync(Guid deadLetterEventId, bool dryRun, CancellationToken ct = default);
}

public record DeadLetterOperationResult(bool Success, string? Error = null)
{
    public static DeadLetterOperationResult Ok() => new(true);
    public static DeadLetterOperationResult Fail(string error) => new(false, error);
}

/// <summary>Wraps the raw dead-lettered JSON payload so it can be re-published as an IIntegrationEvent without ATLAS needing to know its original concrete type.</summary>
internal record ReplayedEnvelope(Guid EventId, string EventType, int Version, DateTimeOffset TimestampUtc,
    Guid CorrelationId, Guid? CausationId, string Producer, string RawPayloadJson) : IIntegrationEvent;

public class DeadLetterService : IDeadLetterService
{
    private readonly EventPlatformDbContext _db;
    private readonly IEventPublisher? _publisher; // optional: null if Kafka isn't configured

    public DeadLetterService(EventPlatformDbContext db, IEventPublisher? publisher = null)
    {
        _db = db;
        _publisher = publisher;
    }

    public async Task RouteToDeadLetterAsync(string topic, Guid eventId, string eventType, Guid correlationId, string payloadJson, string failureReason, CancellationToken ct = default)
    {
        var existing = await _db.DeadLetterEvents.FirstOrDefaultAsync(d => d.EventId == eventId && d.OriginalTopic == topic, ct);
        if (existing is not null)
        {
            existing.RecordAdditionalFailure(failureReason);
        }
        else
        {
            _db.DeadLetterEvents.Add(DeadLetterEvent.Create(topic, eventId, eventType, correlationId, payloadJson, failureReason));
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeadLetterEvent>> ListAsync(string? topic, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var query = _db.DeadLetterEvents.Where(d => !d.Replayed);
        if (!string.IsNullOrWhiteSpace(topic)) query = query.Where(d => d.OriginalTopic == topic);
        return await query.AsNoTracking().OrderByDescending(d => d.LastFailedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync(ct);
    }

    public async Task<DeadLetterOperationResult> MarkReplayedAsync(Guid deadLetterEventId, CancellationToken ct = default)
    {
        var entry = await _db.DeadLetterEvents.FirstOrDefaultAsync(d => d.Id == deadLetterEventId, ct);
        if (entry is null) return DeadLetterOperationResult.Fail("Dead-letter event not found.");
        entry.MarkReplayed();
        await _db.SaveChangesAsync(ct);
        return DeadLetterOperationResult.Ok();
    }

    public async Task<DeadLetterOperationResult> ReplayAsync(Guid deadLetterEventId, bool dryRun, CancellationToken ct = default)
    {
        var entry = await _db.DeadLetterEvents.FirstOrDefaultAsync(d => d.Id == deadLetterEventId, ct);
        if (entry is null) return DeadLetterOperationResult.Fail("Dead-letter event not found.");
        if (entry.Replayed) return DeadLetterOperationResult.Fail("This event was already replayed.");

        if (dryRun)
        {
            // Real dry-run: validates the entry is replayable without side
            // effects — does not publish, does not mark replayed.
            return _publisher is null
                ? DeadLetterOperationResult.Fail("DRY RUN: would fail — no IEventPublisher configured (Kafka:BootstrapServers not set).")
                : DeadLetterOperationResult.Ok();
        }

        if (_publisher is null)
            return DeadLetterOperationResult.Fail("Cannot replay: no IEventPublisher configured (Kafka:BootstrapServers not set).");

        var envelope = new ReplayedEnvelope(entry.EventId, entry.EventType, 1, DateTimeOffset.UtcNow,
            entry.CorrelationId, causationId: entry.Id, Producer: "atlas.eventplatform.replay", entry.PayloadJson);

        await _publisher.PublishAsync(entry.OriginalTopic, envelope, ct);
        entry.MarkReplayed();
        await _db.SaveChangesAsync(ct);
        return DeadLetterOperationResult.Ok();
    }
}
