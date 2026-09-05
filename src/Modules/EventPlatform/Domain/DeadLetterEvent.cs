using Atlas.Shared.Domain;

namespace Atlas.Modules.EventPlatform.Domain;

public class DeadLetterEvent : Entity
{
    public string OriginalTopic { get; private set; } = string.Empty;
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid CorrelationId { get; private set; }
    public string PayloadJson { get; private set; } = string.Empty;
    public string FailureReason { get; private set; } = string.Empty;
    public int RetryCount { get; private set; }
    public DateTimeOffset FirstFailedAtUtc { get; private set; }
    public DateTimeOffset LastFailedAtUtc { get; private set; }
    public bool Replayed { get; private set; }

    private DeadLetterEvent() { }

    public static DeadLetterEvent Create(string originalTopic, Guid eventId, string eventType, Guid correlationId,
        string payloadJson, string failureReason)
    {
        var now = DateTimeOffset.UtcNow;
        return new DeadLetterEvent
        {
            OriginalTopic = originalTopic, EventId = eventId, EventType = eventType, CorrelationId = correlationId,
            PayloadJson = payloadJson, FailureReason = failureReason, RetryCount = 1,
            FirstFailedAtUtc = now, LastFailedAtUtc = now
        };
    }

    public void RecordAdditionalFailure(string reason)
    {
        RetryCount++;
        LastFailedAtUtc = DateTimeOffset.UtcNow;
        FailureReason = reason;
    }

    public void MarkReplayed() => Replayed = true;
}
