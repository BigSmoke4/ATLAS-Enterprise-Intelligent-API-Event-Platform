using Atlas.Shared.Contracts;

namespace Atlas.Modules.DeploymentIntelligence.Application;

/// <summary>
/// Published when a deployment is recorded. The payload carries everything a
/// consumer needs to attribute later telemetry to this version — the envelope
/// itself (event id, version, correlation/causation, producer) comes from
/// <see cref="IIntegrationEvent"/> so idempotency and tracing work uniformly.
/// </summary>
public sealed record DeploymentRecordedIntegrationEvent(
    Guid EventId,
    string EventType,
    int Version,
    DateTimeOffset TimestampUtc,
    Guid CorrelationId,
    Guid? CausationId,
    string Producer,
    Guid OrganizationId,
    Guid DeploymentId,
    Guid ServiceId,
    string DeploymentVersion,
    string Environment,
    string CommitSha,
    string Author) : IIntegrationEvent
{
    public const string TypeName = "DeploymentRecorded";
    public const int CurrentVersion = 1;

    public static DeploymentRecordedIntegrationEvent Create(
        Guid organizationId, Guid deploymentId, Guid serviceId, string version,
        string environment, string commitSha, string author, Guid? correlationId = null)
        => new(
            Guid.NewGuid(),
            TypeName,
            CurrentVersion,
            DateTimeOffset.UtcNow,
            correlationId ?? Guid.NewGuid(),
            CausationId: null,
            Producer: "DeploymentIntelligence",
            organizationId,
            deploymentId,
            serviceId,
            version,
            environment,
            commitSha,
            author);
}
