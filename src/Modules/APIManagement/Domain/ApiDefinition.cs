using Atlas.Shared.Domain;

namespace Atlas.Modules.APIManagement.Domain;

public class ApiDefinition : TenantEntity
{
    public string Name { get; private set; } = string.Empty;
    public string BasePath { get; private set; } = string.Empty; // e.g. "/orders"
    public bool IsActive { get; private set; } = true;

    private readonly List<ApiVersion> _versions = new();
    public IReadOnlyCollection<ApiVersion> Versions => _versions.AsReadOnly();

    private ApiDefinition() { }

    public static ApiDefinition Create(Guid organizationId, string name, string basePath)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("API name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(basePath) || !basePath.StartsWith('/'))
            throw new ArgumentException("Base path must start with '/'.", nameof(basePath));

        return new ApiDefinition { OrganizationId = organizationId, Name = name, BasePath = basePath.TrimEnd('/') };
    }

    public ApiVersion AddVersion(int versionNumber)
    {
        if (_versions.Any(v => v.VersionNumber == versionNumber))
            throw new InvalidOperationException($"Version v{versionNumber} already exists for {Name}.");

        var version = ApiVersion.Create(OrganizationId, Id, versionNumber);
        _versions.Add(version);
        Touch();
        return version;
    }

    public void Deactivate() { IsActive = false; Touch(); }
}
