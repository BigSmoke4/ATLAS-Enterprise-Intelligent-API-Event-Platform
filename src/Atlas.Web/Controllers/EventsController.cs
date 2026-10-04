using Atlas.Modules.EventPlatform.Application;
using Atlas.Shared.Contracts;
using Atlas.Web.Models;
using System.Text.Json;
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
    private readonly IRawEventPublisher? _rawPublisher;
    public EventsController(IDeadLetterService deadLetters, IRawEventPublisher? rawPublisher = null) { _deadLetters = deadLetters; _rawPublisher = rawPublisher; }

    public sealed record PublishRequest(string Topic, Guid EventId, string EventType, int Version, DateTimeOffset TimestampUtc, Guid CorrelationId, Guid? CausationId, string Producer, JsonElement Payload);

    [HttpPost("publish")]
    [Authorize(Policy = "Role:Developer")]
    public async Task<IActionResult> Publish(PublishRequest request, CancellationToken ct)
    {
        if (_rawPublisher is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails { Title = "Kafka publisher is not configured." });
        var payload = JsonSerializer.Serialize(new { request.EventId, request.EventType, request.Version, request.TimestampUtc, request.CorrelationId, request.CausationId, request.Producer, Payload = request.Payload });
        try
        {
            await _rawPublisher.PublishRawAsync(request.Topic, payload, request.EventType, request.Version, request.EventId, request.CorrelationId, request.CausationId, request.Producer, request.TimestampUtc, ct);
            return Accepted(new { request.EventId, request.Topic });
        }
        catch (ArgumentException ex) { return BadRequest(new ProblemDetails { Title = ex.Message }); }
    }

    [HttpGet("dead-letters")]
    public async Task<IActionResult> ListDeadLetters([FromQuery] string? topic, [FromQuery] string? eventType,
        [FromQuery] DateTimeOffset? fromUtc, [FromQuery] DateTimeOffset? toUtc, [FromQuery] Guid? eventId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? sortBy = null, [FromQuery] string? sortDirection = null, CancellationToken ct = default)
    {
        if (!SortQuery.TryResolve(DeadLetterSorting.DeadLetters, sortBy, sortDirection, out var direction, out var error)) return error!;
        return Ok(await _deadLetters.ListAsync(topic, page, pageSize, ct, eventType, fromUtc, toUtc, eventId, sortBy, direction));
    }

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
