using Atlas.Modules.DeploymentIntelligence.Domain;

namespace Atlas.Modules.DeploymentIntelligence.Application;

public record RegressionAnalysisOutcome(bool AnalysisAvailable, string? Reason, RegressionAnalysisResult? Result);

public interface IDeploymentRegressionService
{
    Task<Result<Guid>> RecordDeploymentAsync(Guid organizationId, Guid serviceId, string version, string environment,
        string commitSha, string author, CancellationToken ct = default);

    Task<IReadOnlyList<Deployment>> ListAsync(Guid organizationId, Guid? serviceId, int page = 1, int pageSize = 50, CancellationToken ct = default);

    /// <summary>
    /// Pulls real before/after MetricSample windows from Observability (via
    /// ISloService.GetSamplesAsync — never touches ObservabilityDbContext
    /// directly) and runs RegressionAnalyzer against them.
    /// </summary>
    Task<RegressionAnalysisOutcome> AnalyzeRegressionAsync(Guid organizationId, Guid deploymentId, TimeSpan window, CancellationToken ct = default);
}

public record Result<T>(bool IsSuccess, T? Value, string? Error)
{
    public static Result<T> Success(T value) => new(true, value, null);
    public static Result<T> Failure(string error) => new(false, default, error);
}
