using Atlas.Shared.Contracts;
using Microsoft.AspNetCore.Http;

namespace Atlas.Modules.Organizations.Infrastructure;

/// <summary>Reads the active organization from the authenticated user's claims.</summary>
public class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid? CurrentOrganizationId
    {
        get
        {
            var claim = _accessor.HttpContext?.User?.FindFirst("org_id")?.Value;
            return Guid.TryParse(claim, out var id) ? id : null;
        }
    }

    public bool HasOrganization => CurrentOrganizationId.HasValue;
}
