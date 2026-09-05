using Microsoft.AspNetCore.Identity;

namespace Atlas.Modules.Identity.Domain;

/// <summary>
/// ASP.NET Core Identity user extended with the fields ATLAS needs
/// (current organization context, display name; lockout is already
/// provided by the base class).
/// </summary>
public class AtlasUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>The organization this user is currently acting within (users may belong to more than one).</summary>
    public Guid? CurrentOrganizationId { get; set; }

    public bool IsActive { get; set; } = true;
}

public class AtlasRole : IdentityRole<Guid>
{
    public string? Description { get; set; }
}
