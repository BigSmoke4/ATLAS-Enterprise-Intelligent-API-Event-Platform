using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.DeploymentIntelligence.Application;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>Thin controller — all logic, including the cross-module regression pull, lives in IDeploymentRegressionService.</summary>
[ApiController]
[Route("api/v1/deployments")]
[Authorize]
public class DeploymentsController : ControllerBase
{
    private readonly IDeploymentRegressionService _deployments;
    public DeploymentsController(IDeploymentRegressionService deployments) => _deployments = deployments;

    public record RecordDeploymentRequest(Guid OrganizationId, Guid ServiceId, string Version, string Environment, string CommitSha, string Author);

    [HttpGet]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> List([FromQuery] Guid organizationId, [FromQuery] Guid? serviceId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _deployments.ListAsync(organizationId, serviceId, page, pageSize, ct));

    [HttpPost]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> Record([FromBody] RecordDeploymentRequest request, CancellationToken ct)
    {
        var result = await _deployments.RecordDeploymentAsync(request.OrganizationId, request.ServiceId, request.Version,
            request.Environment, request.CommitSha, request.Author, ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error });
        return Ok(new { deploymentId = result.Value });
    }

    /// <summary>Real regression check — see IDeploymentRegressionService.AnalyzeRegressionAsync for what's actually computed.</summary>
    [HttpGet("{deploymentId:guid}/regression-analysis")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> AnalyzeRegression(Guid deploymentId, [FromQuery] Guid organizationId,
        [FromQuery] int windowMinutes = 15, CancellationToken ct = default)
    {
        var outcome = await _deployments.AnalyzeRegressionAsync(organizationId, deploymentId, TimeSpan.FromMinutes(windowMinutes), ct);
        return Ok(outcome);
    }
}
