using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Core;

/// <summary>
/// Contract every module implements. The host discovers modules via the
/// [assembly: MedClinicModule] attribute and calls these two methods at startup.
/// </summary>
public interface IModule
{
    void RegisterServices(IServiceCollection services, IConfiguration configuration);
    void MapEndpoints(IEndpointRouteBuilder app);
}
