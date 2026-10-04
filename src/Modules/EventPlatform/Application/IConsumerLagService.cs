namespace Atlas.Modules.EventPlatform.Application;

/// <summary>Lag for one topic partition of the ATLAS consumer group.</summary>
public sealed record PartitionLag(string Topic, int Partition, long CommittedOffset, long HighWatermark, string ConsumerGroup)
{
    /// <summary>False when the broker has no committed offset yet (-1001) — that is "unknown", not "zero lag".</summary>
    public bool Available => CommittedOffset >= 0 && HighWatermark >= 0;

    public long Lag => Available ? Math.Max(0, HighWatermark - CommittedOffset) : 0;
}

public sealed record ConsumerLagSnapshot(
    bool Available,
    string Reason,
    string ConsumerGroup,
    DateTimeOffset? ReportedAtUtc,
    IReadOnlyList<PartitionLag> Partitions)
{
    public long TotalLag => Partitions.Where(p => p.Available).Sum(p => p.Lag);

    public TimeSpan? Age => ReportedAtUtc is null ? null : DateTimeOffset.UtcNow - ReportedAtUtc.Value;
}

/// <summary>
/// Written by the Kafka consumer from inside its own poll loop. Lag can only
/// be measured accurately by a member of the consumer group itself: probing a
/// group from outside (admin API) would either join the group — triggering a
/// rebalance of the real consumers — or report committed offsets for a group
/// that is not running. Reporting from the live consumer avoids both problems.
/// </summary>
public interface IConsumerLagReporter
{
    void Report(IReadOnlyList<PartitionLag> partitions);
}

public interface IConsumerLagService
{
    ConsumerLagSnapshot GetSnapshot();
}
