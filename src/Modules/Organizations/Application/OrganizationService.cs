using Atlas.Modules.Organizations.Domain;
using Atlas.Modules.Organizations.Infrastructure;
using Atlas.Shared.Application;
using Microsoft.EntityFrameworkCore;

namespace Atlas.Modules.Organizations.Application;

public sealed class OrganizationService : IOrganizationService
{
    private readonly OrganizationsDbContext _db;
    public OrganizationService(OrganizationsDbContext db) => _db = db;

    public async Task<Result<Guid>> CreateAsync(string name, string slug, CancellationToken ct = default)
    {
        try
        {
            var organization = Organization.Create(name.Trim(), slug.Trim());
            if (await _db.Organizations.AnyAsync(o => o.Slug == organization.Slug, ct))
                return Result.Failure<Guid>("That organization slug is already in use.", "CONFLICT");

            _db.Organizations.Add(organization);
            await _db.SaveChangesAsync(ct);
            return Result.Success(organization.Id);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR");
        }
    }

    public Task<OrganizationDto?> GetAsync(Guid organizationId, CancellationToken ct = default)
        => _db.Organizations.AsNoTracking()
            .Where(o => o.Id == organizationId)
            .Select(o => new OrganizationDto(o.Id, o.Name, o.Slug, o.IsActive, o.Teams.Count, o.Environments.Count))
            .SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<OrganizationDto>> ListAsync(int page = 1, int pageSize = 50, CancellationToken ct = default,
        string? sortBy = null, SortDirection sortDirection = SortDirection.Ascending)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        return await OrganizationSorting.Organizations
            .Apply(_db.Organizations.AsNoTracking(), sortBy, sortDirection)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new OrganizationDto(o.Id, o.Name, o.Slug, o.IsActive, o.Teams.Count, o.Environments.Count))
            .ToListAsync(ct);
    }

    public async Task<Result<Guid>> AddTeamAsync(Guid organizationId, string name, CancellationToken ct = default)
    {
        var organization = await _db.Organizations.SingleOrDefaultAsync(o => o.Id == organizationId, ct);
        if (organization is null) return Result.Failure<Guid>("Organization was not found.", "NOT_FOUND");
        try
        {
            var team = organization.AddTeam(name.Trim());
            await _db.SaveChangesAsync(ct);
            return Result.Success(team.Id);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<Guid>(ex.Message, "VALIDATION_ERROR");
        }
    }
}
