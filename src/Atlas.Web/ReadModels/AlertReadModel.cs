using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.Observability.Application;
using Atlas.Modules.Reliability.Application;
using Atlas.Modules.Reliability.Domain;
using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Domain;

namespace Atlas.Web.ReadModels;

public sealed record AlertItem(
    string Id,
    string Severity,
    string Source,
    string Summary,
    string? Detail,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyDictionary<string, string> Evidence);

public sealed record AlertSnapshot(
    Guid OrganizationId,
    DateTimeOffset GeneratedAtUtc,
    bool HasEvidence,
    string EvidenceSource,
    IReadOnlyList<AlertItem> Alerts);

/// <summary>
/// Derives the active alert list from state the platform actually holds:
/// service registry health, live circuit-breaker states, SLO compliance
/// computed from recorded telemetry, Kafka consumer lag and the dead-letter
/// backlog. Nothing here is synthesised — when none of those sources reports a
/// problem the result is an empty list with <c>HasEvidence = true</c>, which
/// means "checked and clear", not "no data".
///
/// Alert ids are deterministic (source + subject) so a client can de-duplicate
/// across refreshes without server-side state.
/// </summary>
public sealed class AlertReadModel
{
    private const long ConsumerLagWarningThreshold = 10_000;
    private const double ErrorBudgetWarningPercent = 10d;

    private readonly IServiceHealthService _services;
    private readonly ISloService _slos;
    private readonly ICircuitBreakerRegistry _breakers;
    private readonly IConsumerLagService _consumerLag;
    private readonly IDeadLetterService _deadLetters;

    public AlertReadModel(
        IServiceHealthService services,
        ISloService slos,
        ICircuitBreakerRegistry breakers,
        IConsumerLagService consumerLag,
        IDeadLetterService deadLetters)
    {
        _services = services;
        _slos = slos;
        _breakers = breakers;
        _consumerLag = consumerLag;
        _deadLetters = deadLetters;
    }

    public async Task<AlertSnapshot> BuildAsync(Guid organizationId, CancellationToken ct = default)
    {
        var alerts = new List<AlertItem>();
        var now = DateTimeOffset.UtcNow;
        var sourcesConsulted = new List<string>();
        var anySourceReadable = false;

        if (organizationId != Guid.Empty)
        {
            try
            {
                var statuses = await _services.GetStatusAsync(organizationId, 1, 200, ct);
                anySourceReadable = true;
                sourcesConsulted.Add("service-registry");

                foreach (var status in statuses.Where(s => s.AggregateHealth is ServiceHealth.Unhealthy or ServiceHealth.Degraded))
                {
                    var critical = status.AggregateHealth == ServiceHealth.Unhealthy;
                    alerts.Add(new AlertItem(
                        $"service:{status.ServiceId}:{status.AggregateHealth}",
                        critical ? "Critical" : "Warning",
                        "ServiceRegistry",
                        $"{status.Name} is {status.AggregateHealth.ToString().ToLowerInvariant()}",
                        $"{status.HealthyInstanceCount} of {status.InstanceCount} instance(s) healthy.",
                        now,
                        new Dictionary<string, string>
                        {
                            ["serviceId"] = status.ServiceId.ToString(),
                            ["health"] = status.AggregateHealth.ToString(),
                            ["healthyInstances"] = status.HealthyInstanceCount.ToString(),
                            ["instances"] = status.InstanceCount.ToString()
                        }));
                }
            }
            catch (Exception ex)
            {
                sourcesConsulted.Add($"service-registry:unavailable ({ex.GetType().Name})");
            }

            try
            {
                var definitions = await _slos.ListSlosAsync(organizationId, ct);
                anySourceReadable = true;
                sourcesConsulted.Add("slo-compliance");

                foreach (var definition in definitions.Take(50))
                {
                    var compliance = await _slos.GetComplianceAsync(organizationId, definition.SloId, ct);
                    if (compliance is null || !compliance.HasData) continue;

                    var breaching = !compliance.IsCompliant;
                    var budgetLow = compliance.ErrorBudgetRemainingPercent < ErrorBudgetWarningPercent;
                    if (!breaching && !budgetLow) continue;

                    alerts.Add(new AlertItem(
                        $"slo:{definition.SloId}:{(breaching ? "breach" : "budget")}",
                        breaching ? "Critical" : "Warning",
                        "SLO",
                        breaching
                            ? $"SLO '{definition.Name}' is breaching its target"
                            : $"SLO '{definition.Name}' has under {ErrorBudgetWarningPercent:0}% error budget left",
                        $"Current {compliance.CurrentValue:0.###} vs target {compliance.TargetValue:0.###} · burn rate {compliance.BurnRatePerHour:0.##}/h · {compliance.SampleCount} sample(s) · source {compliance.EvidenceSource}.",
                        now,
                        new Dictionary<string, string>
                        {
                            ["sloId"] = definition.SloId.ToString(),
                            ["serviceId"] = definition.ServiceId.ToString(),
                            ["metricType"] = definition.MetricType.ToString(),
                            ["target"] = definition.TargetValue.ToString("0.###"),
                            ["current"] = compliance.CurrentValue.ToString("0.###"),
                            ["errorBudgetRemainingPercent"] = compliance.ErrorBudgetRemainingPercent.ToString("0.##"),
                            ["burnRatePerHour"] = compliance.BurnRatePerHour.ToString("0.##"),
                            ["evidenceSource"] = compliance.EvidenceSource
                        }));
                }
            }
            catch (Exception ex)
            {
                sourcesConsulted.Add($"slo-compliance:unavailable ({ex.GetType().Name})");
            }
        }

        // Circuit breakers live in-process; their state is real but not
        // tenant-scoped, so they are reported with an explicit platform scope.
        try
        {
            var states = _breakers.SnapshotStates();
            anySourceReadable = true;
            sourcesConsulted.Add("circuit-breakers");

            foreach (var breaker in states.Where(kv => kv.Value != CircuitState.Closed))
            {
                alerts.Add(new AlertItem(
                    $"breaker:{breaker.Key}:{breaker.Value}",
                    breaker.Value == CircuitState.Open ? "Critical" : "Warning",
                    "CircuitBreaker",
                    $"Circuit breaker '{breaker.Key}' is {breaker.Value.ToString().ToLowerInvariant()}",
                    "Calls guarded by this breaker are being short-circuited until it closes.",
                    now,
                    new Dictionary<string, string> { ["breaker"] = breaker.Key, ["state"] = breaker.Value.ToString() }));
            }
        }
        catch (Exception ex)
        {
            sourcesConsulted.Add($"circuit-breakers:unavailable ({ex.GetType().Name})");
        }

        try
        {
            var lag = _consumerLag.GetSnapshot();
            sourcesConsulted.Add("consumer-lag");

            if (lag is not null && lag.Available && lag.TotalLag > ConsumerLagWarningThreshold)
            {
                alerts.Add(new AlertItem(
                    $"consumer-lag:{lag.ConsumerGroup ?? "consumer"}:{lag.TotalLag}",
                    "Warning",
                    "EventPlatform",
                    $"Kafka consumer lag is {lag.TotalLag:N0} messages",
                    $"Consumer group '{lag.ConsumerGroup ?? "unknown"}' reported at {lag.ReportedAtUtc:u}.",
                    lag.ReportedAtUtc ?? now,
                    new Dictionary<string, string>
                    {
                        ["totalLag"] = lag.TotalLag.ToString(),
                        ["consumerGroup"] = lag.ConsumerGroup ?? "unknown"
                    }));
            }
        }
        catch (Exception ex)
        {
            sourcesConsulted.Add($"consumer-lag:unavailable ({ex.GetType().Name})");
        }

        try
        {
            // Dead letters are platform-level: an unreplayed message means a
            // real handler failure somewhere, regardless of tenant.
            var deadLetters = await _deadLetters.ListAsync(topic: null, page: 1, pageSize: 200, ct: ct);
            var unreplayed = deadLetters.Count(entry => !entry.Replayed);
            sourcesConsulted.Add("dead-letters");

            if (unreplayed > 0)
            {
                alerts.Add(new AlertItem(
                    $"dead-letters:{unreplayed}",
                    unreplayed > 25 ? "Critical" : "Warning",
                    "EventPlatform",
                    $"{unreplayed} event(s) in the dead-letter queue",
                    "These messages exhausted their consumer retry budget and need inspection or replay.",
                    deadLetters.Count > 0 ? deadLetters.Max(entry => entry.LastFailedAtUtc) : now,
                    new Dictionary<string, string> { ["unreplayed"] = unreplayed.ToString() }));
            }
        }
        catch (Exception ex)
        {
            sourcesConsulted.Add($"dead-letters:unavailable ({ex.GetType().Name})");
        }

        var ordered = alerts
            .OrderByDescending(alert => alert.Severity == "Critical")
            .ThenBy(alert => alert.Source, StringComparer.Ordinal)
            .ThenBy(alert => alert.Id, StringComparer.Ordinal)
            .ToList();

        return new AlertSnapshot(
            organizationId,
            now,
            anySourceReadable,
            $"derived from: {string.Join(", ", sourcesConsulted)}",
            ordered);
    }
}
