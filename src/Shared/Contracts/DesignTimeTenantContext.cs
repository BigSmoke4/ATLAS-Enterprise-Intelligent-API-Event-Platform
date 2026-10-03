namespace Atlas.Shared.Contracts;

/// <summary>Design-time tenant context used only by EF migration scaffolding.</summary>
public sealed class DesignTimeTenantContext : ITenantContext
{
    public Guid? CurrentOrganizationId => null;
    public bool HasOrganization => false;
}
