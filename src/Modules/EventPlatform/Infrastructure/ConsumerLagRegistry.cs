using Atlas.Modules.EventPlatform.Application;

namespace Atlas.Modules.EventPlatform.Infrastructure;

/// <summary>
/// In-memory holder for the most recent lag report from the live consumer.
///
/// Staleness is explicit: if the consumer stops reporting (stopped, crashed,
/// broker unreachable) the snapshot is marked unavailable after
/// <see cref="StaleAfter"/> instead of continuing to serve a stale number as
/// if it were current. That distinction is the difference between monitoring
/// and fiction.
/// </summary>
public sealed class ConsumerLagRegistry : IConsumerLagReporter, IConsumerLagService
{
    public static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(90);

    private readonly object _gate = new();
    private readonly string _consumerGroup;
    private IReadOnlyList<PartitionLag> _partitions = Array.Empty<PartitionLag>();
    private DateTimeOffset? _reportedAtUtc;

    public ConsumerLagRegistry(string consumerGroup) => _consumerGroup = consumerGroup;

    public void Report(IReadOnlyList<PartitionLag> partitions)
    {
        lock (_gate)
        {
            _partitions = partitions;
            _reportedAtUtc = DateTimeOffset.UtcNow;
        }
    }

    public ConsumerLagSnapshot GetSnapshot()
    {
        lock (_gate)
        {
            if (_reportedAtUtc is null)
            {
                return new ConsumerLagSnapshot(false, "No consumer has reported lag yet (Kafka consumer not running or not configured).", _consumerGroup, null, Array.Empty<PartitionLag>());
            }

            var age = DateTimeOffset.UtcNow - _reportedAtUtc.Value;
            if (age > StaleAfter)
            {
                return new ConsumerLagSnapshot(false, $"Last lag report is stale ({age.TotalSeconds:F0}s old; threshold {StaleAfter.TotalSeconds:F0}s).", _consumerGroup, _reportedAtUtc, _partitions);
            }

            if (_partitions.Count == 0)
            {
                return new ConsumerLagSnapshot(false, "Consumer group has no assigned partitions.", _consumerGroup, _reportedAtUtc, _partitions);
            }

            return new ConsumerLagSnapshot(true, "Reported by the live consumer.", _consumerGroup, _reportedAtUtc, _partitions);
        }
    }
}
