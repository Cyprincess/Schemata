using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Abstractions;

namespace Schemata.Authorization.Foundation.Features;

public interface IAuthorizationFeature : IFeature
{
    void ConfigureServer(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder);

    void ConfigureServerAspNetCore(
        IReadOnlyList<IAuthorizationFeature> features,
        IServiceCollection                   services,
        OpenIddictServerBuilder              builder,
        OpenIddictServerAspNetCoreBuilder    integration);
}
