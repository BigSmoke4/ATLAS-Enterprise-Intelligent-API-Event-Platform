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
}
