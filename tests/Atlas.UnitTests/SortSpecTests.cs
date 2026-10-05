using Atlas.Modules.APIManagement.Application;
using Atlas.Modules.APIManagement.Domain;
using Atlas.Modules.Audit.Application;
using Atlas.Modules.DeploymentIntelligence.Application;
using Atlas.Modules.EventPlatform.Application;
using Atlas.Modules.IncidentManagement.Application;
using Atlas.Modules.Organizations.Application;
using Atlas.Modules.PolicyEngine.Application;
using Atlas.Modules.ServiceRegistry.Application;
using Atlas.Shared.Application;
using Xunit;

namespace Atlas.UnitTests;

/// <summary>
/// The list endpoints' sorting contract: a whitelisted field reorders the
/// query, unknown fields can never reach the database, and every shipped spec
/// is wired consistently (whitelist, default field, both directions).
/// </summary>
public sealed class SortSpecTests
{
    private sealed record Row(string Name, int Score);

    private static readonly SortSpec<Row> Rows = SortSpec<Row>.Create(
        defaultField: "name",
        defaultOrdering: q => q.OrderBy(r => r.Name).ThenBy(r => r.Score),
        ("name", (desc, q) => desc ? q.OrderByDescending(r => r.Name) : q.OrderBy(r => r.Name)),
        ("score", (desc, q) => desc ? q.OrderByDescending(r => r.Score) : q.OrderBy(r => r.Score)));

    [Theory]
    [InlineData(null, SortDirection.Ascending)]
    [InlineData("", SortDirection.Ascending)]
    [InlineData("   ", SortDirection.Ascending)]
    [InlineData("asc", SortDirection.Ascending)]
    [InlineData("ASCENDING", SortDirection.Ascending)]
    [InlineData(" desc ", SortDirection.Descending)]
    [InlineData("Descending", SortDirection.Descending)]
    public void Direction_parser_accepts_the_documented_values(string? value, SortDirection expected)
    {
        Assert.True(SortDirections.TryParse(value, out var direction));
        Assert.Equal(expected, direction);
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("down")]
    [InlineData("1")]
    public void Direction_parser_rejects_anything_else(string value)
        => Assert.False(SortDirections.TryParse(value, out _));

    [Fact]
    public void Missing_field_keeps_the_default_ordering()
        => Assert.Equal(new[] { "a", "b" }, Rows.Apply(Query(("b", 1), ("a", 1)), null).Select(r => r.Name));

    [Fact]
    public void Whitelisted_field_sorts_in_both_directions()
    {
        var ascending = Rows.Apply(Query(("b", 2), ("a", 1), ("c", 3)), "name", SortDirection.Ascending).Select(r => r.Name);
        var descending = Rows.Apply(Query(("b", 2), ("a", 1), ("c", 3)), "name", SortDirection.Descending).Select(r => r.Name);

        Assert.Equal(new[] { "a", "b", "c" }, ascending);
        Assert.Equal(new[] { "c", "b", "a" }, descending);
    }

    [Fact]
    public void Field_names_are_case_insensitive()
    {
        Assert.True(Rows.IsKnown("SCORE"));
        Assert.Equal(new[] { 1, 2 }, Rows.Apply(Query(("a", 2), ("b", 1)), "SCORE").Select(r => r.Score));
    }

    [Fact]
    public void Unknown_field_is_never_silently_ignored()
    {
        Assert.False(Rows.IsKnown("password"));
        Assert.Throws<ArgumentOutOfRangeException>(() => { Rows.Apply(Query(("a", 1)), "password").ToList(); });
    }

    [Fact]
    public void Default_fields_match_the_documented_endpoint_defaults()
    {
        Assert.Equal("name", ApiCatalogSorting.Apis.DefaultField);
        Assert.Equal("path", ApiCatalogSorting.Routes.DefaultField);
        Assert.Equal("name", ServiceRegistrySorting.Services.DefaultField);
        Assert.Equal("createdAtUtc", AuditSorting.Entries.DefaultField);
        Assert.Equal("detectedAtUtc", IncidentSorting.Incidents.DefaultField);
        Assert.Equal("deployedAtUtc", DeploymentSorting.Deployments.DefaultField);
        Assert.Equal("lastFailedAtUtc", DeadLetterSorting.DeadLetters.DefaultField);
        Assert.Equal("name", PolicySorting.Rules.DefaultField);
        Assert.Equal("name", OrganizationSorting.Organizations.DefaultField);
    }

    [Fact]
    public void Every_shipped_spec_is_wired_consistently()
    {
        AssertSpec(ApiCatalogSorting.Apis);
        AssertSpec(ApiCatalogSorting.Routes);
        AssertSpec(ServiceRegistrySorting.Services);
        AssertSpec(AuditSorting.Entries);
        AssertSpec(IncidentSorting.Incidents);
        AssertSpec(DeploymentSorting.Deployments);
        AssertSpec(DeadLetterSorting.DeadLetters);
        AssertSpec(PolicySorting.Rules);
        AssertSpec(OrganizationSorting.Organizations);
    }

    [Fact]
    public void Api_catalog_sorting_reorders_real_aggregates()
    {
        var organizationId = Guid.NewGuid();
        var zebra = ApiDefinition.Create(organizationId, "Zebra", "/zebra");
        var alpha = ApiDefinition.Create(organizationId, "Alpha", "/alpha");
        var query = new[] { zebra, alpha }.AsQueryable();

        Assert.Equal(new[] { "Alpha", "Zebra" },
            ApiCatalogSorting.Apis.Apply(query, "name", SortDirection.Ascending).Select(a => a.Name));
        Assert.Equal(new[] { "Zebra", "Alpha" },
            ApiCatalogSorting.Apis.Apply(query, "basePath", SortDirection.Descending).Select(a => a.Name));
    }

    private static void AssertSpec<T>(SortSpec<T> spec)
    {
        Assert.NotEmpty(spec.Fields);
        Assert.Contains(spec.DefaultField, spec.Fields);
        Assert.True(spec.IsKnown(null));
        Assert.False(spec.IsKnown("not_a_field"));
        Assert.Throws<ArgumentOutOfRangeException>(() => { spec.Apply(Array.Empty<T>().AsQueryable(), "not_a_field"); });

        // Every whitelisted field must build a valid ordering in both directions.
        foreach (var field in spec.Fields)
        {
            Assert.Empty(spec.Apply(Array.Empty<T>().AsQueryable(), field, SortDirection.Ascending));
            Assert.Empty(spec.Apply(Array.Empty<T>().AsQueryable(), field, SortDirection.Descending));
        }
    }

    private static IQueryable<Row> Query(params (string Name, int Score)[] rows)
        => rows.Select(row => new Row(row.Name, row.Score)).AsQueryable();
}
