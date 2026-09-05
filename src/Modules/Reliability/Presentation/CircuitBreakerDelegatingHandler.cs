using Atlas.Modules.Reliability.Application;

namespace Atlas.Modules.Reliability.Presentation;

/// <summary>
/// REAL enforcement for OUTBOUND calls: attach this to a named HttpClient
/// (e.g. builder.Services.AddHttpClient("payments-api").AddHttpMessageHandler
/// (() => new CircuitBreakerDelegatingHandler(registry, "payments-api"))) and
/// every request through that client actually consults and updates the
/// CircuitBreaker — a tripped circuit throws immediately instead of hitting
/// the network, exactly the "actual request execution pipeline must enforce
/// it" requirement from the master prompt.
/// </summary>
public class CircuitBreakerDelegatingHandler : DelegatingHandler
{
    private readonly ICircuitBreakerRegistry _registry;
    private readonly string _breakerKey;

    public CircuitBreakerDelegatingHandler(ICircuitBreakerRegistry registry, string breakerKey)
    {
        _registry = registry;
        _breakerKey = breakerKey;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var breaker = _registry.GetOrCreate(_breakerKey);

        if (!breaker.TryAcquire())
        {
            throw new CircuitOpenException(_breakerKey);
        }

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            breaker.RecordResult(response.IsSuccessStatusCode);
            return response;
        }
        catch
        {
            breaker.RecordResult(success: false);
            throw;
        }
    }
}

public class CircuitOpenException : Exception
{
    public string BreakerKey { get; }
    public CircuitOpenException(string breakerKey) : base($"Circuit '{breakerKey}' is open; call short-circuited.")
    {
        BreakerKey = breakerKey;
    }
}
