using Atlas.Modules.Identity.Domain;
using Atlas.Modules.Identity.Presentation;
using Atlas.Shared.Security;
using Atlas.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Atlas.Web.Controllers;

/// <summary>
/// Thin MVC account surface for the browser (cookie) flow. Password
/// verification, lockout, and cookie issuance belong to ASP.NET Core
/// Identity's SignInManager; this controller only translates the form post
/// and returns views. The JSON equivalents live in IdentityController.
/// </summary>
public sealed class AccountController : Controller
{
    private readonly UserManager<AtlasUser> _users;
    private readonly SignInManager<AtlasUser> _signIn;
    private readonly IAuthenticationSchemeProvider _schemes;
    private readonly OidcOptions _oidc;

    public AccountController(UserManager<AtlasUser> users, SignInManager<AtlasUser> signIn,
        IAuthenticationSchemeProvider schemes, IOptions<OidcOptions> oidc)
    {
        _users = users;
        _signIn = signIn;
        _schemes = schemes;
        _oidc = oidc.Value;
    }

    [HttpGet, AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null, string? ssoError = null)
    {
        if (!string.IsNullOrEmpty(ssoError))
        {
            ModelState.AddModelError(string.Empty, ssoError switch
            {
                "notlinked" => "That identity signed in successfully, but no active ATLAS account is linked to its e-mail address. Ask an administrator to provision the account.",
                _ => "Single sign-on did not complete. Try again, or sign in with your ATLAS password."
            });
        }

        return View(new LoginViewModel
        {
            ReturnUrl = returnUrl,
            // The link only appears when the handler is actually registered, so the
            // page never offers a sign-in route the host cannot serve.
            SsoEnabled = await _schemes.GetSchemeAsync(IdentityOidcExtension.SchemeName) is not null,
            SsoDisplayName = _oidc.DisplayName
        });
    }

    /// <summary>
    /// Starts the federated sign-in. 404 — not a redirect — when the operator has
    /// not configured a provider, so an unconfigured deployment cannot advertise a
    /// broken flow.
    /// </summary>
    [HttpGet("account/oidc"), AllowAnonymous]
    public async Task<IActionResult> Oidc(string? returnUrl = null)
    {
        var scheme = await _schemes.GetSchemeAsync(IdentityOidcExtension.SchemeName);
        if (scheme is null)
            return NotFound(new ProblemDetails { Title = "External sign-in is not configured on this deployment." });

        return Challenge(new AuthenticationProperties
        {
            // Open-redirect guard: only local destinations survive the round trip.
            RedirectUri = !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Dashboard"
        }, scheme.Name);
    }

    [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _users.FindByEmailAsync(model.Email);
        // Identical error for unknown user, disabled account, and bad
        // password — login responses must not disclose which case occurred.
        if (user is not null && user.IsActive)
        {
            var result = await _signIn.CheckPasswordSignInAsync(user, model.Password, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                await _signIn.SignInAsync(user, model.RememberMe);
                // Open-redirect guard: only local return URLs are honored.
                return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl)
                    ? LocalRedirect(model.ReturnUrl)
                    : RedirectToAction("Index", "Dashboard");
            }
            if (result.IsLockedOut)
            {
                ModelState.AddModelError(string.Empty, "This account is temporarily locked after repeated failed sign-in attempts.");
                return View(model);
            }
        }

        ModelState.AddModelError(string.Empty, "Invalid credentials.");
        return View(model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Login");
    }

    [HttpGet]
    public IActionResult Denied() => View();
}
