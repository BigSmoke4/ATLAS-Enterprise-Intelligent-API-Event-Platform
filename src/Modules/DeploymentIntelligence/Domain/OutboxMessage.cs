using Atlas.Shared.Contracts;
using Atlas.Shared.Domain;
using System.Text.Json;

namespace Atlas.Modules.DeploymentIntelligence.Domain;

/// <summary>
/// Transactional outbox row. The integration event is written in the *same*
/// <c>SaveChanges</c> as the business state change, so the two are atomic: if
/// the deployment is durable the event is too, and if the transaction rolls
/// back neither exists. A background relay publishes pending rows and retries
/// with exponential backoff, which is what turns "Kafka was briefly down" from
/// "the event is lost" into "the event is late".
///
/// Delivery is **at least once**: a crash between publishing and marking the row
/// sent re-publishes on the next pass. Consumers are idempotent by design (the
/// unique <c>(ConsumerGroup, EventId)</c> index in EventPlatform), which is the
/// other half of this contract.
///
/// The row is inserted once and updated by the relay, so it declares no
/// concurrency token (<c>RowVersion</c> is ignored in the DbContext): leasing is
/// expressed by <see cref="NextAttemptAtUtc"/> instead, which also works when
/// two relay instances run against the same schema.
/// </summary>
public class OutboxMessage : Entity
{
    public string Topic { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public int Version { get; private set; }
    public Guid EventId { get; private set; }
    public Guid CorrelationId { get; private set; }
    public Guid? CausationId { get; private set; }
    public string Producer { get; private set; } = string.Empty;

    /// <summary>The event's own timestamp (not the row's creation time).</summary>
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public string PayloadJson { get; private set; } = string.Empty;

    /// <summary>Publish attempts that failed. Reset is not possible by design.</summary>
    public int Attempts { get; private set; }

    /// <summary>Earliest time the relay may pick this row up (doubles as the lease).</summary>
    public DateTimeOffset NextAttemptAtUtc { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    /// <summary>Set when retries are exhausted: the row stays for inspection but is never retried.</summary>
    public DateTimeOffset? AbandonedAtUtc { get; private set; }

    public string? LastError { get; private set; }

    private OutboxMessage() { }

    /// <summary>
    /// Enqueue an integration event. The envelope is validated first, so an
    /// event that could never be published is rejected at write time rather
    /// than discovered by a relay hours later.
    /// </summary>
    public static OutboxMessage Create<TEvent>(string topic, TEvent @event) where TEvent : IIntegrationEvent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        EventContractValidator.Validate(@event);

        return new OutboxMessage
        {
            Topic = topic,
            EventType = @event.EventType,
            Version = @event.Version,
            EventId = @event.EventId,
            CorrelationId = @event.CorrelationId,
            CausationId = @event.CausationId,
            Producer = @event.Producer,
            OccurredAtUtc = @event.TimestampUtc,
            // Same serializer settings as the direct publish path, so a replayed
            // payload is byte-for-byte what a direct publish would have sent.
            PayloadJson = JsonSerializer.Serialize(@event),
            NextAttemptAtUtc = DateTimeOffset.UtcNow,
        };
    }

    public bool IsPending(DateTimeOffset now) =>
        SentAtUtc is null && AbandonedAtUtc is null && NextAttemptAtUtc <= now;

    public bool IsAbandoned => AbandonedAtUtc is not null;

    public void MarkSent(DateTimeOffset now)
    {
        SentAtUtc = now;
        LastError = null;
        Touch();
    }

    /// <summary>
    /// Record a failed publish attempt. Attempts are counted even when the row
    /// is abandoned, and the error text is truncated to the column width.
    /// </summary>
    public void RecordFailure(string error, TimeSpan nextDelay, int maxAttempts, DateTimeOffset now)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;

        if (Attempts >= maxAttempts) AbandonedAtUtc = now;
        else NextAttemptAtUtc = now + nextDelay;

        Touch();
    }

    private const int MaxErrorLength = 2000;
}
