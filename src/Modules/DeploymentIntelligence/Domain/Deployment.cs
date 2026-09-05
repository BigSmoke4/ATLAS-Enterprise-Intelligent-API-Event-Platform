using Atlas.Shared.Domain;

namespace Atlas.Modules.DeploymentIntelligence.Domain;

public enum DeploymentStatus { InProgress, Succeeded, Failed, RolledBack }

public class Deployment : TenantEntity
{
    public Guid ServiceId { get; private set; }
    public string Version { get; private set; } = string.Empty;
    public string Environment { get; private set; } = string.Empty;
    public string CommitSha { get; private set; } = string.Empty;
    public string Author { get; private set; } = string.Empty;
    public DateTimeOffset DeployedAtUtc { get; private set; }
    public DeploymentStatus Status { get; private set; } = DeploymentStatus.InProgress;

    private Deployment() { }

    public static Deployment Record(Guid organizationId, Guid serviceId, string version, string environment, string commitSha, string author)
    {
        if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("Version is required.", nameof(version));
        if (string.IsNullOrWhiteSpace(commitSha)) throw new ArgumentException("Commit SHA is required.", nameof(commitSha));

        return new Deployment
        {
            OrganizationId = organizationId, ServiceId = serviceId, Version = version,
            Environment = environment, CommitSha = commitSha, Author = author,
            DeployedAtUtc = DateTimeOffset.UtcNow
        };
    }

    public void MarkSucceeded() => Status = DeploymentStatus.Succeeded;
    public void MarkFailed() => Status = DeploymentStatus.Failed;
    public void MarkRolledBack() => Status = DeploymentStatus.RolledBack;
}
