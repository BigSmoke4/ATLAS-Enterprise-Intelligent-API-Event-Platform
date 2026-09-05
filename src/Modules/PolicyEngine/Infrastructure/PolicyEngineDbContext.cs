using Atlas.Modules.PolicyEngine.Domain;
using Atlas.Shared.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using System.Text.Json;

namespace Atlas.Modules.PolicyEngine.Infrastructure;

public class PolicyEngineDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public PolicyEngineDbContext(DbContextOptions<PolicyEngineDbContext> options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<PolicyRule> Rules => Set<PolicyRule>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasDefaultSchema("policyengine");

        var conditionsConverter = new ValueConverter<IReadOnlyCollection<PolicyCondition>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<PolicyCondition>>(v, (JsonSerializerOptions?)null) ?? new List<PolicyCondition>());

        var actionConverter = new ValueConverter<PolicyAction, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<PolicyAction>(v, (JsonSerializerOptions?)null)!);

        builder.Entity<PolicyRule>(b =>
        {
            b.ToTable("PolicyRules");
            b.HasKey(r => r.Id);
            b.Property(r => r.Name).HasMaxLength(256).IsRequired();
            // Conditions/Action are stored as JSON — still pure data, never
            // an executable expression string. See PolicyCondition for why.
            b.Property(r => r.Conditions).HasConversion(conditionsConverter).HasColumnType("jsonb");
            b.Property(r => r.Action).HasConversion(actionConverter).HasColumnType("jsonb");
            b.HasIndex(r => new { r.OrganizationId, r.IsActive });
            b.HasQueryFilter(r => !_tenantContext.HasOrganization || r.OrganizationId == _tenantContext.CurrentOrganizationId);
        });
    }
}
