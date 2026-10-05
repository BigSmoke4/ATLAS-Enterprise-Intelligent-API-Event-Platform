using Atlas.Modules.DeploymentIntelligence.Domain;

namespace Atlas.Modules.DeploymentIntelligence.Application;

/// <summary>
/// HTTP contract for a deployment. The API serializes this projection rather
/// than the EF aggregate, so a persistence change (a new column, the row
/// version) cannot silently alter the published wire format.
/// </summary>
public sealed record DeploymentDto(
    Guid Id,
    Guid ServiceId,
    string Version,
    string Environment,
    string CommitSha,
    string Author,
    DateTimeOffset DeployedAtUtc,
    DeploymentStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc)
{
    public static DeploymentDto From(Deployment deployment) => new(
        deployment.Id,
        deployment.ServiceId,
        deployment.Version,
        deployment.Environment,
        deployment.CommitSha,
        deployment.Author,
        deployment.DeployedAtUtc,
        deployment.Status,
        deployment.CreatedAtUtc,
        deployment.UpdatedAtUtc);
}
