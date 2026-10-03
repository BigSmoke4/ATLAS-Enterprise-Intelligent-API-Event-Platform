using System.Security.Claims;
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

    public IdentityController(UserManager<AtlasUser> users, SignInManager<AtlasUser> signIn, IApiKeyService apiKeys)
    { _users = users; _signIn = signIn; _apiKeys = apiKeys; }

    public sealed record RegisterRequest(string Email, string Password, string DisplayName, Guid OrganizationId);
    public sealed record LoginRequest(string Email, string Password, bool RememberMe = false);
    public sealed record CreateApiKeyRequest(Guid OrganizationId, string Name, DateTimeOffset? ExpiresAtUtc);

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

    [Authorize(Policy = "Role:OrganizationAdmin"), HttpPost("api-keys/{apiKeyId:guid}/revoke")]
    public async Task<IActionResult> RevokeApiKey(Guid apiKeyId, [FromQuery] Guid organizationId, CancellationToken ct)
    {
        if (!CanAccess(organizationId)) return Forbid();
        var result = await _apiKeys.RevokeAsync(organizationId, apiKeyId, ct);
        return result.IsSuccess ? NoContent() : NotFound(new ProblemDetails { Title = result.Error });
    }

    private bool CanAccess(Guid organizationId) => User.IsInRole(AtlasRoles.PlatformAdmin) ||
        Guid.TryParse(User.FindFirstValue("org_id"), out var callerOrg) && callerOrg == organizationId;
}
