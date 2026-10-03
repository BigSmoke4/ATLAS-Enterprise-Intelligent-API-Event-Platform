namespace Atlas.Modules.TrafficManagement.Domain;

public enum RoutingStrategyType { RoundRobin, Weighted, LeastConnections, LatencyBased, Priority, Canary, BlueGreen }

/// <summary>
/// Per-service routing configuration. Weights only apply to the Weighted
/// strategy; other strategies ignore them and use real-time health/latency
/// data instead — see RoutingStrategies.
/// </summary>
public record RoutingPolicy(RoutingStrategyType Strategy, IReadOnlyDictionary<string, int>? Weights = null, IReadOnlyDictionary<string, int>? Priorities = null);
