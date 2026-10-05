using Atlas.Shared.Application;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Models;

/// <summary>
/// Shared handling for the list endpoints' <c>sortBy</c>/<c>sortDirection</c>
/// query parameters. Every paginated list action validates through this helper
/// so an unknown field is rejected identically (a 400 ProblemDetails that names
/// the allowed values) instead of being silently ignored — a caller must never
/// receive data that looks sorted but is not.
/// </summary>
public static class SortQuery
{
    /// <summary>
    /// Returns the concrete <see cref="BadRequestObjectResult"/> (not
    /// <c>IActionResult</c>) so callers whose action returns
    /// <c>ActionResult&lt;T&gt;</c> can return it directly — the implicit
    /// conversion exists for <c>ActionResult</c>, not for the interface.
    /// </summary>
    public static bool TryResolve<T>(SortSpec<T> spec, string? sortBy, string? sortDirection,
        out SortDirection direction, out BadRequestObjectResult? error)
    {
        direction = SortDirection.Ascending;
        error = null;

        if (!spec.IsKnown(sortBy))
        {
            error = new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Unknown sort field.",
                Detail = $"'{sortBy}' is not a sortable field. Allowed sortBy values: {string.Join(", ", spec.Fields)}.",
                Status = StatusCodes.Status400BadRequest,
                Extensions =
                {
                    ["code"] = "INVALID_SORT_FIELD",
                    ["allowedFields"] = spec.Fields
                }
            });
            return false;
        }

        if (!SortDirections.TryParse(sortDirection, out direction))
        {
            error = new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Invalid sort direction.",
                Detail = $"'{sortDirection}' is not a valid sortDirection. Allowed values: asc, desc.",
                Status = StatusCodes.Status400BadRequest,
                Extensions =
                {
                    ["code"] = "INVALID_SORT_DIRECTION",
                    ["allowedDirections"] = new[] { "asc", "desc" }
                }
            });
            return false;
        }

        return true;
    }
}
