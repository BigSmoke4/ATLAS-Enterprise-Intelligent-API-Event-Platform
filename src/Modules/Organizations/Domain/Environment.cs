using Atlas.Shared.Domain;

namespace Atlas.Modules.Organizations.Domain;

public enum EnvironmentTier { Development, Staging, Production }

public class Environment : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public EnvironmentTier Tier { get; private set; }

    private Environment() { }

    public static Environment Create(Guid organizationId, string name, EnvironmentTier tier)
        => new() { OrganizationId = organizationId, Name = name, Tier = tier };
}
