using Atlas.Shared.Domain;

namespace Atlas.Modules.Organizations.Domain;

public class Organization : Entity
{
    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public bool IsActive { get; private set; } = true;

    private readonly List<Team> _teams = new();
    public IReadOnlyCollection<Team> Teams => _teams.AsReadOnly();

    private readonly List<Domain.Environment> _environments = new();
    public IReadOnlyCollection<Domain.Environment> Environments => _environments.AsReadOnly();

    private Organization() { }

    public static Organization Create(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Organization name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Organization slug is required.", nameof(slug));

        var org = new Organization { Name = name, Slug = slug.ToLowerInvariant() };
        org.SeedDefaultEnvironments();
        return org;
    }

    private void SeedDefaultEnvironments()
    {
        _environments.Add(Domain.Environment.Create(Id, "Development", EnvironmentTier.Development));
        _environments.Add(Domain.Environment.Create(Id, "Staging", EnvironmentTier.Staging));
        _environments.Add(Domain.Environment.Create(Id, "Production", EnvironmentTier.Production));
    }

    public Team AddTeam(string name)
    {
        var team = Team.Create(Id, name);
        _teams.Add(team);
        Touch();
        return team;
    }

    public void Deactivate() { IsActive = false; Touch(); }
}
