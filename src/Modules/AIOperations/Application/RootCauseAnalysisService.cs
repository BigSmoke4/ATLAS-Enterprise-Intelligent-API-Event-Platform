using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.IncidentManagement.Application;
using Atlas.Modules.IncidentManagement.Domain;
using Atlas.Modules.Observability.Application;
using Atlas.Modules.Observability.Domain;
using Atlas.Modules.ServiceRegistry.Application;

namespace Atlas.Modules.AIOperations.Application;

public sealed class RootCauseAnalysisService : IRootCauseAnalysisService
{
    private readonly IIncidentService _incidents;
    private readonly IServiceHealthService _services;
    private readonly ISloService _slo;
    private readonly IDeploymentRegressionService _deployments;

    public RootCauseAnalysisService(IIncidentService incidents, IServiceHealthService services, ISloService slo, IDeploymentRegressionService deployments)
    { _incidents = incidents; _services = services; _slo = slo; _deployments = deployments; }

    public async Task<RootCauseAnalysisResult> AnalyzeIncidentAsync(Guid organizationId, Guid incidentId, CancellationToken ct = default)
    {
        var incident = await _incidents.GetAsync(organizationId, incidentId, ct);
        if (incident is null) return Insufficient("Incident was not found in the requested organization.");
        if (incident.AffectedServiceIds.Count == 0) return Insufficient("The incident has no affected service relationships.");

        var evidence = new List<RootCauseEvidence>();
        var candidates = new List<(Guid ServiceId, string Cause, double Score)>();
        var statuses = await _services.GetStatusAsync(organizationId, 1, 100, ct);
        foreach (var serviceId in incident.AffectedServiceIds.Distinct())
        {
            var status = statuses.FirstOrDefault(s => s.ServiceId == serviceId);
            var score = 0d;
            if (status is not null && (status.AggregateHealth is ServiceHealth.Unhealthy or ServiceHealth.Unavailable))
            {
                score += .25;
                evidence.Add(new RootCauseEvidence("service-registry", $"Service {serviceId} is currently {status.AggregateHealth} with {status.HealthyInstanceCount}/{status.InstanceCount} healthy instances.", DateTimeOffset.UtcNow));
            }

            var deployments = await _deployments.ListAsync(organizationId, serviceId, 1, 10, ct);
            var recentDeployment = deployments.FirstOrDefault(d => d.DeployedAtUtc >= incident.StartedAtUtc.AddHours(-1) && d.DeployedAtUtc <= incident.DetectedAtUtc.AddMinutes(15));
            if (recentDeployment is not null)
            {
                score += .35;
                evidence.Add(new RootCauseEvidence("deployment-history", $"Deployment {recentDeployment.Version} ({recentDeployment.CommitSha}) occurred at {recentDeployment.DeployedAtUtc:O} near incident detection.", recentDeployment.DeployedAtUtc));
                var from = recentDeployment.DeployedAtUtc.AddMinutes(-15);
                var to = recentDeployment.DeployedAtUtc.AddMinutes(15);
                var before = await _slo.GetSamplesAsync(organizationId, serviceId, SloMetricType.ErrorRate, from, recentDeployment.DeployedAtUtc, ct);
                var after = await _slo.GetSamplesAsync(organizationId, serviceId, SloMetricType.ErrorRate, recentDeployment.DeployedAtUtc, to, ct);
                if (before.Count > 0 && after.Count > 0)
                {
                    var beforeRate = before.Count(s => s.Success == false) / (double)before.Count;
                    var afterRate = after.Count(s => s.Success == false) / (double)after.Count;
                    if (afterRate > beforeRate && (beforeRate == 0 || afterRate >= beforeRate * 1.2))
                    {
                        score += .4;
                        evidence.Add(new RootCauseEvidence("error-telemetry", $"Error rate increased from {beforeRate:P2} to {afterRate:P2} around deployment {recentDeployment.Version}.", recentDeployment.DeployedAtUtc));
                    }
                }
                candidates.Add((serviceId, $"Deployment {recentDeployment.Version} may have introduced a regression in service {serviceId}.", score));
            }
            else if (status is not null && (status.AggregateHealth is ServiceHealth.Unhealthy or ServiceHealth.Unavailable))
            {
                candidates.Add((serviceId, $"Service {serviceId} health degradation is the strongest available signal.", score));
            }
        }

        if (candidates.Count == 0 || evidence.Count == 0) return Insufficient("No correlated deployment, health, or error telemetry was found.");
        var best = candidates.OrderByDescending(c => c.Score).First();
        var confidence = Math.Clamp(best.Score, 0, 1);
        return new RootCauseAnalysisResult(true, best.Cause, confidence, new[] { best.ServiceId }, evidence);
    }

    private static RootCauseAnalysisResult Insufficient(string reason)
        => new(false, "Insufficient evidence.", 0, Array.Empty<Guid>(), new[] { new RootCauseEvidence("analysis", reason) });
}
