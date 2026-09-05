namespace Atlas.Modules.Reliability.Domain;

public enum CircuitState { Closed, Open, HalfOpen }

/// <summary>
/// Real, sampling-window circuit breaker state machine (not UI-only).
/// Intended to be invoked from the actual outbound request pipeline
/// (e.g. an HttpClient DelegatingHandler or the traffic-routing engine)
/// so a tripped circuit genuinely short-circuits calls.
/// </summary>
public class CircuitBreaker
{
    private readonly object _sync = new();
    private readonly Queue<(DateTimeOffset Timestamp, bool Success)> _window = new();

    public string Key { get; }
    public double FailureThreshold { get; }
    public int MinimumRequestVolume { get; }
    public TimeSpan SamplingDuration { get; }
    public TimeSpan OpenDuration { get; }

    public CircuitState State { get; private set; } = CircuitState.Closed;
    private DateTimeOffset _openedAtUtc;

    public CircuitBreaker(string key, double failureThreshold = 0.5, int minimumRequestVolume = 10,
        TimeSpan? samplingDuration = null, TimeSpan? openDuration = null)
    {
        Key = key;
        FailureThreshold = failureThreshold;
        MinimumRequestVolume = minimumRequestVolume;
        SamplingDuration = samplingDuration ?? TimeSpan.FromSeconds(30);
        OpenDuration = openDuration ?? TimeSpan.FromSeconds(15);
    }

    /// <summary>Call before attempting the guarded operation. Returns false if the call should be short-circuited.</summary>
    public bool TryAcquire()
    {
        lock (_sync)
        {
            Prune(DateTimeOffset.UtcNow);

            if (State == CircuitState.Open)
            {
                if (DateTimeOffset.UtcNow - _openedAtUtc >= OpenDuration)
                {
                    State = CircuitState.HalfOpen;
                    return true; // allow a single trial request through
                }
                return false;
            }

            return true; // Closed or HalfOpen (trial already granted once per open period at app layer)
        }
    }

    /// <summary>Record the outcome of a call made after TryAcquire() returned true.</summary>
    public void RecordResult(bool success)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            _window.Enqueue((now, success));
            Prune(now);

            if (State == CircuitState.HalfOpen)
            {
                State = success ? CircuitState.Closed : ReOpen(now);
                return;
            }

            if (_window.Count >= MinimumRequestVolume)
            {
                var failureRate = _window.Count(e => !e.Success) / (double)_window.Count;
                if (failureRate >= FailureThreshold)
                {
                    ReOpen(now);
                }
            }
        }
    }

    private CircuitState ReOpen(DateTimeOffset now)
    {
        State = CircuitState.Open;
        _openedAtUtc = now;
        return CircuitState.Open;
    }

    private void Prune(DateTimeOffset now)
    {
        while (_window.Count > 0 && now - _window.Peek().Timestamp > SamplingDuration)
        {
            _window.Dequeue();
        }
    }
}
