using Atlas.Modules.EventPlatform.Domain;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.EventPlatform.Infrastructure;

public class EventPlatformDbContext : DbContext
{
    public EventPlatformDbContext(DbContextOptions<EventPlatformDbContext> options) : base(options) { }

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<DeadLetterEvent> DeadLetterEvents => Set<DeadLetterEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("eventplatform");

        builder.Entity<IdempotencyRecord>(b =>
        {
            b.ToTable("IdempotencyRecords");
            b.HasKey(r => r.Id);
            b.Property(r => r.ConsumerGroup).HasMaxLength(256).IsRequired();
            // This unique index is the actual mechanism that prevents a
            // duplicate delivery from re-executing business logic.
            b.HasIndex(r => new { r.ConsumerGroup, r.EventId }).IsUnique();
        });

        builder.Entity<DeadLetterEvent>(b =>
        {
            b.ToTable("DeadLetterEvents");
            b.HasKey(d => d.Id);
            b.Property(d => d.OriginalTopic).HasMaxLength(256).IsRequired();
            b.Property(d => d.EventType).HasMaxLength(256).IsRequired();
            b.Property(d => d.FailureReason).HasMaxLength(2000).IsRequired();
            b.HasIndex(d => d.OriginalTopic);
            b.HasIndex(d => d.EventId);
        });
    }
}
