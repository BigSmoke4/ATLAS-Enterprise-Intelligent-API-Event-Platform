using Atlas.Modules.EventPlatform.Domain;
using Atlas.Modules.EventPlatform.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.EventPlatform.Application;

public class IdempotencyGuard : IIdempotencyGuard
{
    private readonly EventPlatformDbContext _db;
    public IdempotencyGuard(EventPlatformDbContext db) => _db = db;

    public async Task<bool> TryMarkProcessedAsync(string consumerGroup, Guid eventId, CancellationToken ct = default)
    {
        var alreadyProcessed = await _db.IdempotencyRecords
            .AnyAsync(r => r.ConsumerGroup == consumerGroup && r.EventId == eventId, ct);
        if (alreadyProcessed) return false;

        _db.IdempotencyRecords.Add(IdempotencyRecord.Create(consumerGroup, eventId));
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException)
        {
            // Unique-index violation: another concurrent instance processed
            // this event first between our check and our insert. That is
            // the correct outcome — treat it as "already processed".
            return false;
        }
    }
}
