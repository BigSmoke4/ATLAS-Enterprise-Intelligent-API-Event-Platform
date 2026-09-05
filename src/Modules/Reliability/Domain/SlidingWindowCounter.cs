namespace Atlas.Modules.Reliability.Domain;

/// <summary>
/// Sliding-window-log style limiter: exact within the log's retained events,
/// used where fixed-window edge bursts (2x the limit at a window boundary)
/// are unacceptable.
/// </summary>
public class SlidingWindowCounter
{
    private readonly Queue<DateTimeOffset> _events = new();
    public int Limit { get; }
    public TimeSpan Window { get; }

    public SlidingWindowCounter(int limit, TimeSpan window)
    {
        Limit = limit;
        Window = window;
    }

    public bool TryHit(DateTimeOffset nowUtc)
    {
        while (_events.Count > 0 && nowUtc - _events.Peek() > Window)
        {
            _events.Dequeue();
        }

        if (_events.Count >= Limit) return false;

        _events.Enqueue(nowUtc);
        return true;
    }

    public int CurrentCount => _events.Count;
}
