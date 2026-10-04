using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atlas.Shared.Web;

/// <summary>
/// Maps an EF Core optimistic-concurrency failure to <c>409 Conflict</c> with a
/// ProblemDetails body instead of letting it become an opaque 500.
///
/// Every mutable aggregate carries a concurrency token (PostgreSQL's
/// <c>xmin</c>, see <see cref="Atlas.Shared.Domain.Entity.RowVersion"/>), so a
/// write based on a stale read is rejected by the database. Without this
/// mapping the caller would receive a 500 and might retry a write that can
/// never succeed; with it, the contract is explicit: re-read the resource and
/// retry, or discard the edit.
///
/// Registered in the composition root as an <see cref="IExceptionHandler"/>, so
/// it runs only for requests that already passed authentication, anti-forgery
/// and validation (a rejected 400 is never re-labelled 409). Development and
/// Testing hosts keep their developer exception page / direct-throw behaviour.
/// </summary>
public sealed class ConcurrencyConflictExceptionHandler : IExceptionHandler
{
    /// <summary>Stable error code for clients (documented in docs/api.md).</summary>
    public const string ErrorCode = "CONCURRENCY_CONFLICT";

    private readonly ILogger<ConcurrencyConflictExceptionHandler> _logger;

    public ConcurrencyConflictExceptionHandler(ILogger<ConcurrencyConflictExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not DbUpdateConcurrencyException conflict) return false;

        // Names only — enough to tell an operator which aggregate conflicted,
        // without copying row data (which may be tenant-scoped) into logs.
        var entityTypes = conflict.Entries
            .Select(entry => entry.Metadata.ClrType.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        _logger.LogWarning(conflict,
            "Optimistic concurrency conflict on {Method} {Path}: {EntityTypes} changed after the request read them.",
            httpContext.Request.Method, httpContext.Request.Path,
            entityTypes.Length == 0 ? "(unknown entity)" : string.Join(", ", entityTypes));

        httpContext.Response.StatusCode = StatusCodes.Status409Conflict;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Concurrent modification detected.",
            Detail = "The resource changed after this request read it, so the write was rejected rather than overwriting newer data. Re-read the resource and retry the change.",
            Status = StatusCodes.Status409Conflict,
            Instance = httpContext.Request.Path,
            Extensions = { ["code"] = ErrorCode, ["conflictingEntities"] = entityTypes }
        }, contentType: "application/problem+json", cancellationToken: cancellationToken);

        return true;
    }
}
