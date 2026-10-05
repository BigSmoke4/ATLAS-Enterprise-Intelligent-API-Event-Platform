using Atlas.Modules.AIOperations.Application;
using Atlas.Modules.Audit.Domain;
using Atlas.Modules.DeploymentIntelligence.Domain;
using Atlas.Modules.EventPlatform.Domain;
using Atlas.Modules.PolicyEngine.Domain;
using Atlas.Modules.Reliability.Application;
using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Modules.ServiceRegistry.Domain;
using Atlas.Modules.TrafficManagement.Application;
using Atlas.Web.ReadModels;

namespace Atlas.Web.Models;

/// <summary>Services page: registry graph joined with live telemetry and routing policy.</summary>
public sealed class ServiceTopologyViewModel
{
    public bool HasOrganizationContext { get; set; }
    public Guid OrganizationId { get; set; }
    public ServiceTopology Topology { get; set; } = new(Guid.Empty, DateTimeOffset.UtcNow, false, "none", Array.Empty<TopologyNode>());
    public IReadOnlyDictionary<Guid, TrafficPolicyDto?> TrafficPolicies { get; set; } = new Dictionary<Guid, TrafficPolicyDto?>();

    public int TotalInstances => Topology.Nodes.Sum(node => node.Instances);
    public int HealthyInstances => Topology.Nodes.Sum(node => node.HealthyInstances);

    public static string HealthTone(ServiceHealth health) => health switch
    {
        ServiceHealth.Healthy => "ok",
        ServiceHealth.Degraded => "warn",
        ServiceHealth.Unhealthy or ServiceHealth.Unavailable => "bad",
        _ => "idle"
    };

    /// <summary>
    /// Tone for the topology read model, whose health is the recorded
    /// <see cref="ServiceHealth"/> name as a string (the read model is
    /// serialized to the browser as JSON, where enums are names).
    /// </summary>
    public static string HealthTone(string health)
        => Enum.TryParse<ServiceHealth>(health, ignoreCase: true, out var parsed) ? HealthTone(parsed) : "idle";
}

/// <summary>Event platform page: dead-letter backlog plus the replay capability flags.</summary>
public sealed class EventsViewModel
{
    public bool HasOrganizationContext { get; set; }
    public Guid OrganizationId { get; set; }
    public IReadOnlyList<DeadLetterEvent> DeadLetters { get; set; } = Array.Empty<DeadLetterEvent>();
    public bool CanReplay { get; set; }
    public bool CanPublish { get; set; }
}

/// <summary>Deployment intelligence page: recorded deployments and the services they can target.</summary>
public sealed class DeploymentsViewModel
{
    public bool HasOrganizationContext { get; set; }
    public Guid OrganizationId { get; set; }
    public IReadOnlyList<Deployment> Deployments { get; set; } = Array.Empty<Deployment>();
    public IReadOnlyList<ServiceStatusDto> Services { get; set; } = Array.Empty<ServiceStatusDto>();
}

/// <summary>Policy engine page: stored rules and their versions.</summary>
public sealed class PoliciesViewModel
{
    public bool HasOrganizationContext { get; set; }
    public Guid OrganizationId { get; set; }
    public IReadOnlyList<PolicyRule> Policies { get; set; } = Array.Empty<PolicyRule>();
}

/// <summary>Reliability page: circuit-breaker states and the configured limiter surface.</summary>
public sealed class ReliabilityViewModel
{
    public bool HasOrganizationContext { get; set; }
    public Guid OrganizationId { get; set; }
    public IReadOnlyDictionary<string, Atlas.Modules.Reliability.Domain.CircuitState> CircuitBreakers { get; set; }
        = new Dictionary<string, Atlas.Modules.Reliability.Domain.CircuitState>();
    public string RateLimiterBackend { get; set; } = "redis (atomic Lua counters, distributed across instances)";

    public static string ToneFor(Atlas.Modules.Reliability.Domain.CircuitState state) => state switch
    {
        Atlas.Modules.Reliability.Domain.CircuitState.Closed => "ok",
        Atlas.Modules.Reliability.Domain.CircuitState.HalfOpen => "warn",
        _ => "bad"
    };
}

/// <summary>AI operations page: the registered tool surface (read vs action).</summary>
public sealed class AiOpsViewModel
{
    public bool HasOrganizationContext { get; set; }
    public Guid OrganizationId { get; set; }
    public IReadOnlyList<AiToolDescriptor> Tools { get; set; } = Array.Empty<AiToolDescriptor>();
    public bool CompletionProviderConfigured { get; set; }
}

public sealed record AiToolDescriptor(string Name, string Description, string Kind);

/// <summary>Audit page: append-only security/operational trail.</summary>
public sealed class AuditViewModel
{
    public bool HasOrganizationContext { get; set; }
    public Guid OrganizationId { get; set; }
    public IReadOnlyList<AuditEntry> Entries { get; set; } = Array.Empty<AuditEntry>();
    public string? ActionFilter { get; set; }
    public string? ResourceTypeFilter { get; set; }
    public bool CanRead { get; set; }
}
