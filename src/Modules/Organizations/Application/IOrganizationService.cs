using Atlas.Shared.Application;

namespace Atlas.Modules.Organizations.Application;

public interface IOrganizationService
{
    Task<Result<Guid>> CreateAsync(string name, string slug, CancellationToken ct = default);
    Task<OrganizationDto?> GetAsync(Guid organizationId, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationDto>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<Result<Guid>> AddTeamAsync(Guid organizationId, string name, CancellationToken ct = default);
}

public sealed record OrganizationDto(Guid Id, string Name, string Slug, bool IsActive, int TeamCount, int EnvironmentCount);
