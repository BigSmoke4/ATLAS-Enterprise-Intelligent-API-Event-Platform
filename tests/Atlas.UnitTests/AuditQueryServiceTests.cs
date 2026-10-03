using Atlas.Modules.Audit.Application;
using Atlas.Modules.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Atlas.UnitTests;

public sealed class AuditQueryServiceTests
{
    [Fact]
    public async Task Query_is_tenant_scoped_and_supports_resource_and_action_filters()
    {
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var organization = Guid.NewGuid();
        using (var seed = new AuditDbContext(options))
        {
            seed.Entries.Add(Atlas.Modules.Audit.Domain.AuditEntry.Create(null, "test", organization, "dead_letter.replay", "DeadLetterEvent", "one", Guid.NewGuid()));
            seed.Entries.Add(Atlas.Modules.Audit.Domain.AuditEntry.Create(null, "test", organization, "other.action", "PolicyRule", "two", Guid.NewGuid()));
            seed.Entries.Add(Atlas.Modules.Audit.Domain.AuditEntry.Create(null, "test", Guid.NewGuid(), "dead_letter.replay", "DeadLetterEvent", "three", Guid.NewGuid()));
            await seed.SaveChangesAsync();
        }

        using var db = new AuditDbContext(options);
        var service = new AuditQueryService(db);
        var result = await service.ListAsync(organization, "DeadLetterEvent", "dead_letter.replay", 1, 50);

        var entry = Assert.Single(result);
        Assert.Equal("one", entry.ResourceId);
    }
}
