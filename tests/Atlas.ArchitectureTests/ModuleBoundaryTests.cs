using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Atlas.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    private static readonly string[] Assemblies =
    [
        "Atlas.Modules.Identity", "Atlas.Modules.Organizations", "Atlas.Modules.APIManagement",
        "Atlas.Modules.TrafficManagement", "Atlas.Modules.ServiceRegistry", "Atlas.Modules.EventPlatform",
        "Atlas.Modules.Reliability", "Atlas.Modules.Observability", "Atlas.Modules.IncidentManagement",
        "Atlas.Modules.DeploymentIntelligence", "Atlas.Modules.PolicyEngine", "Atlas.Modules.AIOperations",
        "Atlas.Modules.Audit"
    ];

    [Fact]
    public void Domain_and_application_layers_do_not_reference_infrastructure_layers_of_other_modules()
    {
        foreach (var assemblyName in Assemblies)
        {
            var assembly = Assembly.Load(assemblyName);
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespace(assemblyName + ".Domain")
                .Or().ResideInNamespace(assemblyName + ".Application")
                .Should().NotHaveDependencyOnAny(Assemblies
                    .Where(other => other != assemblyName)
                    .Select(other => other + ".Infrastructure").ToArray())
                .GetResult();

            Assert.True(result.IsSuccessful, $"{assemblyName} domain/application references another module infrastructure layer.");
        }
    }

    [Fact]
    public void Every_module_assembly_is_covered_by_the_boundary_sweep()
    {
        // The sweep above is only as strong as its list: a module added to the
        // solution but forgotten here would be silently unpoliced. Enumerate the
        // solution's module projects and require each to appear.
        var solutionDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (solutionDir is not null && !File.Exists(Path.Combine(solutionDir.FullName, "ATLAS.sln")))
            solutionDir = solutionDir.Parent;
        Assert.NotNull(solutionDir);

        var projectModules = Directory.GetFiles(Path.Combine(solutionDir!.FullName, "src", "Modules"), "Atlas.Modules.*.csproj")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var covered = Assemblies.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        Assert.Equal(projectModules, covered);
    }

    [Fact]
    public void Domain_layers_do_not_depend_on_entity_framework()
    {
        // The domain model must be persistence-ignorant: mappings live in each
        // module's Infrastructure/DbContext, so an aggregate can be unit-tested
        // without a database. Identity is not exempt — its Domain type derives
        // from ASP.NET Identity's user, not from EF Core.
        foreach (var assemblyName in Assemblies)
        {
            var assembly = Assembly.Load(assemblyName);
            var result = Types.InAssembly(assembly)
                .That().ResideInNamespace(assemblyName + ".Domain")
                .Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
                .GetResult();

            Assert.True(result.IsSuccessful, $"{assemblyName}.Domain depends on Entity Framework Core.");
        }
    }

    [Fact]
    public void Controllers_are_not_allowed_to_reference_entity_framework()
    {
        // Force the Atlas.Web assembly into the AppDomain — listing loaded
        // assemblies and matching by name is order-dependent and silently
        // finds nothing when nothing has yet touched a Web type.
        var web = typeof(Program).Assembly;
        var result = Types.InAssembly(web).That().ResideInNamespace("Atlas.Web.Controllers")
            .Should().NotHaveDependencyOn("Microsoft.EntityFrameworkCore")
            .GetResult();
        Assert.True(result.IsSuccessful);
    }
}
