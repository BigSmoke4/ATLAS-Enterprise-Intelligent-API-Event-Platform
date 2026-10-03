using Atlas.Shared.Domain;
using System.Security.Cryptography;
using System.Text;

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

    public static Organization Create(string name, string slug, Guid? id = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Organization name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Organization slug is required.", nameof(slug));

        var org = new Organization { Id = id ?? Guid.NewGuid(), Name = name.Trim(), Slug = slug.Trim().ToLowerInvariant() };
        org.SeedDefaultEnvironments();
        return org;
    }

    private void SeedDefaultEnvironments()
    {
        _environments.Add(Domain.Environment.Create(Id, "Development", EnvironmentTier.Development, DeterministicId("development")));
        _environments.Add(Domain.Environment.Create(Id, "Staging", EnvironmentTier.Staging, DeterministicId("staging")));
        _environments.Add(Domain.Environment.Create(Id, "Production", EnvironmentTier.Production, DeterministicId("production")));
    }

    private Guid DeterministicId(string value)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes($"atlas:{Id:N}:{value}"));
        return new Guid(bytes);
    }
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
