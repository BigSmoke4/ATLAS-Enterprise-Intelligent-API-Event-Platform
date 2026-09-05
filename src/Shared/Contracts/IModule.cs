using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Shared.Contracts;

/// <summary>
/// Every module (Identity, Organizations, APIManagement, ...) implements this
/// so Atlas.Web can discover and wire modules without any module referencing
/// another module's internals. This is the seam that would let a module be
/// extracted into its own service later.
/// </summary>
public interface IAtlasModule
{
    string Name { get; }
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void RegisterEndpoints(IEndpointRouteBuilder endpoints);
}
