using Atlas.Shared.Domain;

namespace Atlas.Modules.Organizations.Domain;

public class Team : TenantEntity
{
    public string Name { get; private set; } = string.Empty;

    private Team() { }

    public static Team Create(Guid organizationId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Team name is required.", nameof(name));
        return new Team { OrganizationId = organizationId, Name = name };
    }
}
