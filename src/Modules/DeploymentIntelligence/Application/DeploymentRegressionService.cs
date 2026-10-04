using Atlas.Modules.DeploymentIntelligence.Domain;
using Atlas.Modules.DeploymentIntelligence.Infrastructure;
using Atlas.Modules.Observability.Application;
using Atlas.Modules.Observability.Domain;
using Atlas.Shared.Application;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atlas.Modules.DeploymentIntelligence.Application;

/// <summary>Topic names owned by this module, kept as constants so producer and configuration cannot drift silently.</summary>
public static class DeploymentTopics
{
    public const string Deployments = "atlas.events.deployments";
}

public class DeploymentRegressionService : IDeploymentRegressionService
{
    private readonly DeploymentIntelligenceDbContext _db;
    private readonly ISloService _sloService; // cross-module via Application interface only — never Observability's DbContext
    private readonly IEventPublisher? _publisher; // optional: present only when Kafka is configured
    private readonly ILogger<DeploymentRegressionService>? _logger;

    public DeploymentRegressionService(
        DeploymentIntelligenceDbContext db,
        ISloService sloService,
        IEventPublisher? publisher = null,
        ILogger<DeploymentRegressionService>? logger = null)
    {
        _db = db;
        _sloService = sloService;
        _publisher = publisher;
        _logger = logger;
    }

    public async Task<Result<Guid>> RecordDeploymentAsync(Guid organizationId, Guid serviceId, string version, string environment,
        string commitSha, string author, CancellationToken ct = default)
    {
        try
        {
            var deployment = Deployment.Record(organizationId, serviceId, version, environment, commitSha, author);
            deployment.MarkSucceeded();
            _db.Deployments.Add(deployment);
            await _db.SaveChangesAsync(ct);

            await PublishDeploymentRecordedAsync(deployment, ct);
            return Result<Guid>.Success(deployment.Id);
        }
        catch (ArgumentException ex)
        {
            return Result<Guid>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Publishes the integration event for a recorded deployment.
    ///
    /// The deployment itself is already durable at this point, so a broker
    /// outage is logged and counted — it must never roll back the recording or
    /// pretend the event was delivered. When Kafka is not configured there is
    /// no publisher at all, which is reported the same way (the console's
    /// event pipeline page shows the dead-letter queue and the lag that the
    /// consumer would otherwise report).
    /// </summary>
    private async Task PublishDeploymentRecordedAsync(Deployment deployment, CancellationToken ct)
    {
        if (_publisher is null)
        {
            _logger?.LogDebug("Deployment {DeploymentId} recorded without publishing: no IEventPublisher configured.", deployment.Id);
            return;
        }

        try
        {
            var @event = DeploymentRecordedIntegrationEvent.Create(
                deployment.OrganizationId, deployment.Id, deployment.ServiceId,
                deployment.Version, deployment.Environment, deployment.CommitSha, deployment.Author);

            await _publisher.PublishAsync(DeploymentTopics.Deployments, @event, ct);
            Atlas.Shared.Observability.AtlasMetrics.EventsPublished.Add(1,
                new KeyValuePair<string, object?>("topic", DeploymentTopics.Deployments),
                new KeyValuePair<string, object?>("event_type", DeploymentRecordedIntegrationEvent.TypeName));
        }
        catch (Exception ex)
        {
            Atlas.Shared.Observability.AtlasMetrics.EventsPublished.Add(1,
                new KeyValuePair<string, object?>("topic", DeploymentTopics.Deployments),
                new KeyValuePair<string, object?>("event_type", DeploymentRecordedIntegrationEvent.TypeName),
                new KeyValuePair<string, object?>("outcome", "failed"));
            _logger?.LogWarning(ex, "Deployment {DeploymentId} was recorded but its integration event could not be published.", deployment.Id);
        }
    }

    public async Task<IReadOnlyList<Deployment>> ListAsync(Guid organizationId, Guid? serviceId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var query = _db.Deployments.Where(d => d.OrganizationId == organizationId);
        if (serviceId.HasValue) query = query.Where(d => d.ServiceId == serviceId.Value);
        return await query.OrderByDescending(d => d.DeployedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .AsNoTracking().ToListAsync(ct);
    }

    public async Task<CanaryAnalysisResult> AnalyzeCanaryAsync(Guid organizationId, Guid deploymentId, TimeSpan window, CancellationToken ct = default)
    {
        var deployment = await _db.Deployments.AsNoTracking().FirstOrDefaultAsync(d => d.Id == deploymentId && d.OrganizationId == organizationId, ct);
        if (deployment is null) return new CanaryAnalysisResult(false, false, "Insufficient evidence.", 0, 0, 0, 0, 0, 0, new[] { "Deployment not found." });
        var from = deployment.DeployedAtUtc - window;
        var to = deployment.DeployedAtUtc + window;
        var outcomes = await _sloService.GetSamplesAsync(organizationId, deployment.ServiceId, SloMetricType.ErrorRate, from, to, ct);
        var latency = await _sloService.GetSamplesAsync(organizationId, deployment.ServiceId, SloMetricType.LatencyP95, from, to, ct);
        return CanaryAnalyzer.Analyze(outcomes.Concat(latency).ToList(), deployment.Version);
    }

    public async Task<RegressionAnalysisOutcome> AnalyzeRegressionAsync(Guid organizationId, Guid deploymentId, TimeSpan window, CancellationToken ct = default)
    {
        var deployment = await _db.Deployments.FirstOrDefaultAsync(d => d.Id == deploymentId && d.OrganizationId == organizationId, ct);
        if (deployment is null) return new RegressionAnalysisOutcome(false, "Deployment not found.", null);

        var before = deployment.DeployedAtUtc - window;
        var after = deployment.DeployedAtUtc + window;

        var beforeError = await _sloService.GetSamplesAsync(organizationId, deployment.ServiceId, SloMetricType.ErrorRate, before, deployment.DeployedAtUtc, ct);
        var afterError = await _sloService.GetSamplesAsync(organizationId, deployment.ServiceId, SloMetricType.ErrorRate, deployment.DeployedAtUtc, after, ct);

        if (beforeError.Count == 0 || afterError.Count == 0)
            return new RegressionAnalysisOutcome(false, "Insufficient recorded telemetry on one or both sides of the deployment window.", null);

        var beforeLatency = await _sloService.GetSamplesAsync(organizationId, deployment.ServiceId, SloMetricType.LatencyP95, before, deployment.DeployedAtUtc, ct);
        var afterLatency = await _sloService.GetSamplesAsync(organizationId, deployment.ServiceId, SloMetricType.LatencyP95, deployment.DeployedAtUtc, after, ct);

        var beforeErrorRate = beforeError.Count(s => s.Success == false) / (double)beforeError.Count;
        var afterErrorRate = afterError.Count(s => s.Success == false) / (double)afterError.Count;
        var beforeP95 = Percentile(beforeLatency.Select(s => s.Value).ToList(), 95);
        var afterP95 = Percentile(afterLatency.Select(s => s.Value).ToList(), 95);

        var result = RegressionAnalyzer.Analyze(beforeErrorRate, afterErrorRate, beforeP95, afterP95);
        return new RegressionAnalysisOutcome(true, null, result);
    }

    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        var index = Math.Clamp((int)Math.Ceiling(percentile / 100.0 * sorted.Count) - 1, 0, sorted.Count - 1);
        return sorted[index];
    }
}
