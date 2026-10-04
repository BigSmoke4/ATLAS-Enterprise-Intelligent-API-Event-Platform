using Microsoft.AspNetCore.Authorization;
using Atlas.Modules.Audit.Application;
using Atlas.Web.Models;
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
        [FromQuery] string? action, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDirection = null, CancellationToken ct = default)
    {
        if (!organizationId.HasValue && !User.IsInRole("PlatformAdmin"))
            return BadRequest(new ProblemDetails { Title = "organizationId is required for organization-scoped audit access." });
        if (!SortQuery.TryResolve(AuditSorting.Entries, sortBy, sortDirection, out var direction, out var error)) return error!;
        var entries = await _audit.ListAsync(organizationId, resourceType, action, page, pageSize, ct, sortBy, direction);
        return Ok(entries.Select(AuditEntryDto.From).ToArray());
    }
}
