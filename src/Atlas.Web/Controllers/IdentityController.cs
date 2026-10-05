using System.Security.Claims;
using Atlas.Modules.Audit.Application;
using Atlas.Modules.Identity.Application;
using Atlas.Modules.Identity.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Atlas.Web.Controllers;

[ApiController]
[Route("api/v1/account")]
public sealed class IdentityController : ControllerBase
{
    private readonly UserManager<AtlasUser> _users;
    private readonly SignInManager<AtlasUser> _signIn;
    private readonly IApiKeyService _apiKeys;
    private readonly IAuditLogger _audit;

    public IdentityController(UserManager<AtlasUser> users, SignInManager<AtlasUser> signIn, IApiKeyService apiKeys, IAuditLogger audit)
    { _users = users; _signIn = signIn; _apiKeys = apiKeys; _audit = audit; }

    public sealed record RegisterRequest(string Email, string Password, string DisplayName, Guid OrganizationId);
    public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);
    public sealed record CreateApiKeyRequest(Guid OrganizationId, string Name, DateTimeOffset? ExpiresAtUtc);
    public sealed record RevokeSessionsRequest(Guid UserId);

    [AllowAnonymous, HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct)
    {
        if (request.OrganizationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Email))
            return BadRequest(new ProblemDetails { Title = "Validation failed", Detail = "A valid organization and email are required." });
        var user = new AtlasUser { UserName = request.Email.Trim(), Email = request.Email.Trim(), DisplayName = request.DisplayName.Trim(), CurrentOrganizationId = request.OrganizationId };
        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return BadRequest(new ProblemDetails { Title = "Registration failed", Detail = string.Join("; ", result.Errors.Select(e => e.Description)) });
        await _users.AddToRoleAsync(user, AtlasRoles.Viewer);
        return StatusCode(StatusCodes.Status201Created, new { userId = user.Id });
    }

    [AllowAnonymous, HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null || !user.IsActive) return Unauthorized(new ProblemDetails { Title = "Invalid credentials" });
        var result = await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded) return Unauthorized(new ProblemDetails { Title = "Invalid credentials" });
        await _signIn.SignInAsync(user, request.RememberMe);
        return Ok(new { userId = user.Id, organizationId = user.CurrentOrganizationId });
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { await _signIn.SignOutAsync(); return NoContent(); }

    [Authorize(Policy = "Role:OrganizationAdmin"), HttpPost("api-keys")]
    public async Task<IActionResult> CreateApiKey(CreateApiKeyRequest request, CancellationToken ct)
    {
        if (!CanAccess(request.OrganizationId)) return Forbid();
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return Unauthorized();
        var result = await _apiKeys.CreateAsync(request.OrganizationId, userId, request.Name, request.ExpiresAtUtc, ct);
        if (!result.IsSuccess) return BadRequest(new ProblemDetails { Title = result.Error, Detail = result.ErrorCode });
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    [Authorize(Policy = "Role:OrganizationAdmin"), HttpGet("api-keys")]
    public async Task<IActionResult> ListApiKeys([FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!CanAccess(organizationId)) return Forbid();
        return Ok(await _apiKeys.ListAsync(organizationId, ct));
    }

    [Authorize(Policy = "Role:OrganizationAdmin"), HttpPost("api-keys/{apiKeyId:guid}/revoke")]
    public async Task<IActionResult> RevokeApiKey(Guid apiKeyId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!CanAccess(organizationId)) return Forbid();
        var result = await _apiKeys.RevokeAsync(organizationId, apiKeyId, ct);
        return result.IsSuccess ? NoContent() : NotFound(new ProblemDetails { Title = result.Error });
    }

    // ---------------------------------------------------------------------
    // Session revocation. Cookies are bearer tokens; these endpoints are what
    // makes them revocable (IdentitySessionValidation re-checks the stamp and
    // the active flag on every request). Every call is audited.
    // ---------------------------------------------------------------------

    [Authorize, HttpPost("sessions/revoke-all")]
    public async Task<IActionResult> RevokeOwnSessions(CancellationToken ct)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Unauthorized(new ProblemDetails { Title = "Unknown user" });

        // Rotate the stamp, then clear this caller's own cookie as well: "revoke
        // all" includes the session issuing the request.
        var result = await _users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) return RevocationFailed(result);
        await _signIn.SignOutAsync();
        await AuditAsync("identity.sessions.revoked", user, ct);
        return NoContent();
    }

    [Authorize(Policy = "Role:PlatformAdmin"), HttpPost("users/{userId:guid}/sessions/revoke-all")]
    public async Task<IActionResult> RevokeUserSessions(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new ProblemDetails { Title = "User not found" });
        var result = await _users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) return RevocationFailed(result);
        await AuditAsync("identity.sessions.revoked-by-admin", user, ct);
        return NoContent();
    }

    [Authorize(Policy = "Role:PlatformAdmin"), HttpPost("users/{userId:guid}/deactivate")]
    public async Task<IActionResult> DeactivateUser(Guid userId, CancellationToken ct)
    {
        // No operator may lock themselves out — including by accident.
        if (userId == CurrentUserId())
            return BadRequest(new ProblemDetails
            {
                Title = "Self-deactivation refused",
                Detail = "An operator cannot deactivate their own account; ask another PlatformAdmin."
            });

        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new ProblemDetails { Title = "User not found" });

        user.IsActive = false;
        // One write: UpdateSecurityStampAsync persists the entity *and* rotates
        // the stamp, so the deactivated account's live cookies die immediately
        // rather than at ticket expiry.
        var result = await _users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded) return RevocationFailed(result);
        await AuditAsync("identity.user.deactivated", user, ct);
        return NoContent();
    }

    [Authorize(Policy = "Role:PlatformAdmin"), HttpPost("users/{userId:guid}/reactivate")]
    public async Task<IActionResult> ReactivateUser(Guid userId, CancellationToken ct)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null) return NotFound(new ProblemDetails { Title = "User not found" });

        user.IsActive = true;
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded) return RevocationFailed(result);
        await AuditAsync("identity.user.reactivated", user, ct);
        return NoContent();
    }

    private IActionResult RevocationFailed(IdentityResult result) =>
        BadRequest(new ProblemDetails { Title = "The account could not be updated", Detail = string.Join("; ", result.Errors.Select(e => e.Description)) });

    private Guid? CurrentUserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    private Task AuditAsync(string action, AtlasUser target, CancellationToken ct) =>
        _audit.RecordAsync(CurrentUserId(), User.Identity?.Name ?? "unknown", target.CurrentOrganizationId,
            action, resourceType: "AtlasUser", resourceId: target.Id.ToString(), correlationId: Guid.NewGuid(), ct: ct);

    private bool CanAccess(Guid organizationId) => User.IsInRole(AtlasRoles.PlatformAdmin) ||
        Guid.TryParse(User.FindFirstValue("org_id"), out var callerOrg) && callerOrg == organizationId;
}
