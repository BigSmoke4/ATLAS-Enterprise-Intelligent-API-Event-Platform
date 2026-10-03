using System.Reflection;
using NetArchTest.Rules;
using Xunit;

namespace Atlas.ArchitectureTests;

public sealed class ModuleBoundaryTests
{
    private static readonly string[] Assemblies =
    [
        "Atlas.Modules.Identity", "Atlas.Modules.Organizations", "Atlas.Modules.APIManagement",
        "Atlas.Modules.ServiceRegistry", "Atlas.Modules.EventPlatform", "Atlas.Modules.Reliability",
        "Atlas.Modules.Observability", "Atlas.Modules.IncidentManagement", "Atlas.Modules.DeploymentIntelligence",
        "Atlas.Modules.PolicyEngine", "Atlas.Modules.AIOperations", "Atlas.Modules.Audit"
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
