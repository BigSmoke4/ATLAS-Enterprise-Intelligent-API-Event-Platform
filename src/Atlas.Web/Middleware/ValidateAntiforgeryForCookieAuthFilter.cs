using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Atlas.Web.Middleware;

/// <summary>
/// Antiforgery enforcement for JSON writes made with the **browser cookie**.
///
/// Cross-site request forgery needs an ambient credential: something the
/// browser attaches without the page asking. ATLAS has two authentication
/// paths, and only one of them is ambient:
///
/// * the Identity application cookie — attached automatically to same-site
///   requests, therefore CSRF-reachable, therefore validated here;
/// * an API key in the <c>X-Api-Key</c> header — a machine credential that a
///   cross-site page cannot set, therefore not CSRF-reachable (and machine
///   callers must not be forced to fetch a page token first).
///
/// Razor forms already carry <c>[ValidateAntiForgeryToken]</c>; this filter
/// closes the same hole for the console's module writes, which post JSON to the
/// platform's own API.
/// </summary>
public sealed class ValidateAntiforgeryForCookieAuthFilter : IAsyncAuthorizationFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly IAntiforgery _antiforgery;
    private readonly ILogger<ValidateAntiforgeryForCookieAuthFilter> _logger;

    public ValidateAntiforgeryForCookieAuthFilter(
        IAntiforgery antiforgery,
        ILogger<ValidateAntiforgeryForCookieAuthFilter> logger)
    {
        _antiforgery = antiforgery;
        _logger = logger;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var request = context.HttpContext.Request;

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) ||
            HttpMethods.IsOptions(request.Method) || HttpMethods.IsTrace(request.Method))
            return;

        // Authenticated by an API key: not an ambient browser credential.
        if (request.Headers.ContainsKey(ApiKeyHeader)) return;

        // Only the Identity cookie is CSRF-reachable. Any other authenticated
        // scheme (a bearer token, a test/host scheme, an integration harness) is
        // not attached automatically by a browser, so it is left alone.
        var identity = context.HttpContext.User.Identities.FirstOrDefault(candidate => candidate.IsAuthenticated);
        if (identity is null) return; // anonymous: the authorization middleware returns 401/302 first
        if (!string.Equals(identity.AuthenticationType, IdentityConstants.ApplicationScheme, StringComparison.Ordinal))
            return;

        try
        {
            await _antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException ex)
        {
            _logger.LogWarning(ex, "Rejected {Method} {Path}: antiforgery validation failed for a cookie-authenticated request.",
                request.Method, request.Path);

            context.Result = new BadRequestObjectResult(new ProblemDetails
            {
                Title = "Antiforgery token validation failed.",
                Detail = "Cookie-authenticated writes must send the antiforgery token (X-CSRF-TOKEN header or __RequestVerificationToken).",
                Status = StatusCodes.Status400BadRequest
            });
        }
    }
}
