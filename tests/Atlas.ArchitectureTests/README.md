PLANNED. Add NetArchTest rules here once modules reference each other's
compiled assemblies, e.g.:

    Types.InAssembly(typeof(Atlas.Modules.Organizations.Domain.Organization).Assembly)
        .Should().NotHaveDependencyOn("Atlas.Modules.Identity.Infrastructure")
        .GetResult().IsSuccessful

This wasn't authored as executable tests yet because this environment has
no .NET SDK to compile the module assemblies against.
