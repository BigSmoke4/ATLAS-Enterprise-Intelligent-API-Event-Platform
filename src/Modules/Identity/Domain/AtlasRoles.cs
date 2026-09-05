namespace Atlas.Modules.Identity.Domain;

/// <summary>Fixed role set for ATLAS. Roles are seeded on startup, not user-creatable, to keep RBAC auditable.</summary>
public static class AtlasRoles
{
    public const string PlatformAdmin = "PlatformAdmin";
    public const string OrganizationAdmin = "OrganizationAdmin";
    public const string SRE = "SRE";
    public const string Developer = "Developer";
    public const string SecurityEngineer = "SecurityEngineer";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = new[]
    {
        PlatformAdmin, OrganizationAdmin, SRE, Developer, SecurityEngineer, Operator, Viewer
    };
}
