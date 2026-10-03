using Atlas.Modules.Organizations.Domain;
using Xunit;

namespace Atlas.UnitTests;

public sealed class OrganizationTests
{
    [Fact]
    public void Create_provisions_all_required_environments()
    {
        var organization = Organization.Create("Payments", "PAYMENTS");

        Assert.Equal("payments", organization.Slug);
        Assert.True(organization.IsActive);
        Assert.Equal(3, organization.Environments.Count);
        Assert.Contains(organization.Environments, environment => environment.Tier == EnvironmentTier.Production);
    }

    [Fact]
    public void AddTeam_rejects_empty_names()
    {
        var organization = Organization.Create("Payments", "payments");
        Assert.Throws<ArgumentException>(() => organization.AddTeam(" "));
    }
}
