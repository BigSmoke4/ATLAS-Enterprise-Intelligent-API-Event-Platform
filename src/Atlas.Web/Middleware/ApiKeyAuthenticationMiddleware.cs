using System.Security.Claims;
using Atlas.Modules.Identity.Application;

namespace Atlas.Web.Middleware;

public sealed class ApiKeyAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    public ApiKeyAuthenticationMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IApiKeyService apiKeys)
    {
        if (context.User.Identity?.IsAuthenticated != true && context.Request.Headers.TryGetValue("X-Api-Key", out var rawKey))
        {
            var principal = await apiKeys.AuthenticateAsync(rawKey.ToString(), context.RequestAborted);
            if (principal is not null)
            {
                var identity = new ClaimsIdentity("AtlasApiKey");
                identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, principal.CreatedByUserId.ToString()));
                identity.AddClaim(new Claim("org_id", principal.OrganizationId.ToString()));
                identity.AddClaim(new Claim(ClaimTypes.Role, "Developer"));
                context.User = new ClaimsPrincipal(identity);
            }
        }
        await _next(context);
    }
}
