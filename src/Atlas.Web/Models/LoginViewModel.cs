using System.ComponentModel.DataAnnotations;

namespace Atlas.Web.Models;

/// <summary>View model for the MVC sign-in form (browser cookie flow; the JSON /api/v1/account endpoints remain for API clients).</summary>
public sealed class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }

    /// <summary>True when an OIDC provider is registered, so the form offers the federated route.</summary>
    public bool SsoEnabled { get; set; }

    /// <summary>Label shown on the federated sign-in button (from <c>Oidc:DisplayName</c>).</summary>
    public string SsoDisplayName { get; set; } = "Single sign-on";
}
