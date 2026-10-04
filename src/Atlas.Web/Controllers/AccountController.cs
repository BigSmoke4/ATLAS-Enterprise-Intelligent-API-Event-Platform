using Atlas.Modules.Identity.Domain;
using Atlas.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

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

    public AccountController(UserManager<AtlasUser> users, SignInManager<AtlasUser> signIn)
    {
        _users = users;
        _signIn = signIn;
    }

    [HttpGet, AllowAnonymous]
    public IActionResult Login(string? returnUrl = null)
        => View(new LoginViewModel { ReturnUrl = returnUrl });

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
