using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Authorization.Foundation.Features;

public interface IAuthorizationFeature : IFeature
{
    void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder);

    void ConfigureServerAspNetCore(
        IServiceCollection                services,
        OpenIddictServerBuilder           builder,
        OpenIddictServerAspNetCoreBuilder integration);
}
