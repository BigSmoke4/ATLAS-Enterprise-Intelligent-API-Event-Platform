using Atlas.Modules.Organizations.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

[ApiController]
[Route("api/v1/organizations")]
[Authorize]
public sealed class OrganizationsController : ControllerBase
{
    private readonly IOrganizationService _organizations;
    public OrganizationsController(IOrganizationService organizations) => _organizations = organizations;

    public sealed record CreateOrganizationRequest(string Name, string Slug);
    public sealed record AddTeamRequest(Guid OrganizationId, string Name);

    [HttpGet]
    [Authorize(Policy = "Role:PlatformAdmin")]
    public async Task<ActionResult<IReadOnlyList<OrganizationDto>>> List([FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _organizations.ListAsync(page, pageSize, ct));

    [HttpGet("{organizationId:guid}")]
    [Authorize(Policy = "SameOrganization")]
    public async Task<IActionResult> Get(Guid organizationId, CancellationToken ct)
    {
        var organization = await _organizations.GetAsync(organizationId, ct);
        return organization is null ? NotFound(new ProblemDetails { Title = "Organization was not found." }) : Ok(organization);
    }

    [HttpPost]
    [Authorize(Policy = "Role:PlatformAdmin")]
    public async Task<IActionResult> Create(CreateOrganizationRequest request, CancellationToken ct)
    {
        var result = await _organizations.CreateAsync(request.Name, request.Slug, ct);
        if (!result.IsSuccess)
            return result.ErrorCode == "CONFLICT" ? Conflict(new ProblemDetails { Title = result.Error }) : BadRequest(new ProblemDetails { Title = result.Error });
        return CreatedAtAction(nameof(Get), new { organizationId = result.Value }, new { organizationId = result.Value });
    }

    [HttpPost("{organizationId:guid}/teams")]
    [Authorize(Policy = "Role:OrganizationAdmin")]
    public async Task<IActionResult> AddTeam(Guid organizationId, AddTeamRequest request, CancellationToken ct)
    {
        if (request.OrganizationId != organizationId) return BadRequest(new ProblemDetails { Title = "Organization identifiers do not match." });
        if (!User.IsInRole("PlatformAdmin") && User.FindFirst("org_id")?.Value != organizationId.ToString()) return Forbid();
        var result = await _organizations.AddTeamAsync(organizationId, request.Name, ct);
        if (!result.IsSuccess) return result.ErrorCode == "NOT_FOUND" ? NotFound(new ProblemDetails { Title = result.Error }) : BadRequest(new ProblemDetails { Title = result.Error });
        return StatusCode(StatusCodes.Status201Created, new { teamId = result.Value });
    }
}
