using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.Audit.Application;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>Read-only by design — no write action exists here; AuditDbContext itself also rejects any Modified/Deleted state.</summary>
[ApiController]
[Route("api/v1/audit")]
[Authorize(Policy = "AuditRead")]
[Authorize(Policy = "SameOrganization")]
public class AuditController : ControllerBase
{
    private readonly IAuditQueryService _audit;
    public AuditController(IAuditQueryService audit) => _audit = audit;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? organizationId, [FromQuery] string? resourceType,
        [FromQuery] string? action, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        if (!organizationId.HasValue && !User.IsInRole("PlatformAdmin"))
            return BadRequest(new ProblemDetails { Title = "organizationId is required for organization-scoped audit access." });
        return Ok(await _audit.ListAsync(organizationId, resourceType, action, page, pageSize, ct));
    }
}
