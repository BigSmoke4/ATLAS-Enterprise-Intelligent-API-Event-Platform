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
    public void Every_module_the_host_composes_is_covered_by_the_boundary_sweep()
    {
        // The sweep above is only as strong as its list: a module added to the
        // host but forgotten here would be silently unpoliced. The host
        // constructs every module, so its referenced assemblies are the
        // authoritative module list — no file paths, no drift.
        var composedByHost = typeof(Program).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => name.StartsWith("Atlas.Modules.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        var covered = Assemblies.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        var missing = composedByHost.Except(covered, StringComparer.Ordinal).ToArray();
        var stale = covered.Except(composedByHost, StringComparer.Ordinal).ToArray();
        Assert.True(missing.Length == 0 && stale.Length == 0,
            $"modules composed by the host but not swept: [{string.Join(", ", missing)}]; " +
            $"swept but not composed: [{string.Join(", ", stale)}]");
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
