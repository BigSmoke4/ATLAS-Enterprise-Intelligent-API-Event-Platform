using Atlas.Modules.EventPlatform.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

/// <summary>Real DLQ inspection AND replay surface — Replay actually re-publishes to the original topic via IEventPublisher (see IDeadLetterService.ReplayAsync); dry-run validates without publishing.</summary>
[ApiController]
[Route("api/v1/events")]
[Authorize]
public class EventsController : ControllerBase
{
    private readonly IDeadLetterService _deadLetters;
    public EventsController(IDeadLetterService deadLetters) => _deadLetters = deadLetters;

    [HttpGet("dead-letters")]
    public async Task<IActionResult> ListDeadLetters([FromQuery] string? topic,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _deadLetters.ListAsync(topic, page, pageSize, ct));

    /// <summary>Flags as replayed WITHOUT re-publishing (use when the fix was applied out-of-band).</summary>
    [HttpPost("dead-letters/{id:guid}/mark-replayed")]
    [Authorize(Policy = "Role:SRE")]
    public async Task<IActionResult> MarkReplayed(Guid id, CancellationToken ct)
    {
        var result = await _deadLetters.MarkReplayedAsync(id, ct);
        if (!result.Success) return NotFound(new ProblemDetails { Title = result.Error });
        return NoContent();
    }

    /// <summary>Real replay: dryRun=true validates only; dryRun=false actually re-publishes to the original topic. Requires PlatformAdmin — live replay is a production-affecting action per the master prompt's authorization requirement.</summary>
    [HttpPost("dead-letters/{id:guid}/replay")]
    [Authorize(Policy = "Role:PlatformAdmin")]
    public async Task<IActionResult> Replay(Guid id, [FromQuery] bool dryRun = true, CancellationToken ct = default)
    {
        var result = await _deadLetters.ReplayAsync(id, dryRun, ct);
        if (!result.Success) return UnprocessableEntity(new ProblemDetails { Title = result.Error });
        return Ok(new { dryRun, replayed = !dryRun });
    }
}
