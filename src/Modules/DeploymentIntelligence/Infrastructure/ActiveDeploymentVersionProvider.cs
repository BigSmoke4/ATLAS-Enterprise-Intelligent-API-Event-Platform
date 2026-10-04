using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.DeploymentIntelligence.Infrastructure;

/// <summary>
/// Implements the cross-module read seam used for telemetry attribution: given
/// an organization, a service and a moment in time, returns the version of the
/// most recent deployment at or before that moment.
///
/// This is what makes "error rate rose 3 minutes after deployment v2.8" a fact
/// rather than a coincidence: every request bucket is stamped with the version
/// that was actually live while it was recorded.
///
/// Returns null when the service has never been deployed — Observability then
/// stores an empty version (unattributed), never a guessed one.
/// </summary>
public sealed class ActiveDeploymentVersionProvider : IActiveDeploymentVersionProvider
{
    private readonly DeploymentIntelligenceDbContext _db;

    public ActiveDeploymentVersionProvider(DeploymentIntelligenceDbContext db) => _db = db;

    public async Task<string?> GetActiveVersionAsync(Guid organizationId, Guid serviceId, DateTimeOffset atUtc, CancellationToken ct = default)
        => await _db.Deployments.AsNoTracking()
            .Where(d => d.OrganizationId == organizationId && d.ServiceId == serviceId && d.DeployedAtUtc <= atUtc)
            .OrderByDescending(d => d.DeployedAtUtc)
            .Select(d => d.Version)
            .FirstOrDefaultAsync(ct);
}
