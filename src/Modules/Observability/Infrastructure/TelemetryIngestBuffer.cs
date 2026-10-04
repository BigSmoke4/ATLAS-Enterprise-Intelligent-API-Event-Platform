using System.Threading.Channels;
using Atlas.Modules.Observability.Application;
using Atlas.Shared.Observability;

namespace Atlas.Modules.Observability.Infrastructure;

/// <summary>
/// Bounded, single-consumer channel that decouples the HTTP request path from
/// telemetry persistence. Registered as a singleton; the
/// <see cref="TelemetryAggregationService"/> background service is the only
/// reader.
///
/// Back-pressure policy: <see cref="BoundedChannelFullMode.DropWrite"/>. If the
/// buffer is saturated ATLAS drops the newest observation and increments a
/// counter (exported as <c>atlas.telemetry.samples.dropped</c>) instead of
/// slowing down real traffic. That trade-off is deliberate and observable —
/// silently blocking user requests to record metrics about them would be the
/// wrong engineering call.
/// </summary>
public sealed class TelemetryIngestBuffer : ITelemetryIngestService
{
    public const int DefaultCapacity = 50_000;

    private readonly Channel<RequestTelemetryObservation> _channel;
    private long _accepted;
    private long _processed;
    private long _dropped;

    public TelemetryIngestBuffer(int capacity = DefaultCapacity)
    {
        _channel = Channel.CreateBounded<RequestTelemetryObservation>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public bool TryRecord(in RequestTelemetryObservation observation)
    {
        if (_channel.Writer.TryWrite(observation))
        {
            Interlocked.Increment(ref _accepted);
            return true;
        }

        Interlocked.Increment(ref _dropped);
        AtlasMetrics.TelemetrySamplesDropped.Add(1);
        return false;
    }

    public void MarkProcessed(int count) => Interlocked.Add(ref _processed, count);

    public TelemetryIngestStatistics GetStatistics()
    {
        var accepted = Interlocked.Read(ref _accepted);
        var processed = Interlocked.Read(ref _processed);
        var dropped = Interlocked.Read(ref _dropped);
        return new TelemetryIngestStatistics(accepted, processed, dropped, Math.Max(0, accepted - processed));
    }

    public ValueTask<RequestTelemetryObservation> ReadAsync(CancellationToken ct)
        => _channel.Reader.ReadAsync(ct);

    public bool TryRead(out RequestTelemetryObservation observation)
        => _channel.Reader.TryRead(out observation);
}
