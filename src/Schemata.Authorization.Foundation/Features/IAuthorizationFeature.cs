using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Authorization.Foundation.Features;

public interface IAuthorizationFeature : IFeature
{
    public void ConfigureServer(IServiceCollection services, OpenIddictServerBuilder builder);

    public void ConfigureServerAspNetCore(IServiceCollection services, OpenIddictServerAspNetCoreBuilder builder);
}
