using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Security.Foundation.Providers;
using Schemata.Security.Skeleton;

namespace Schemata.Security.Foundation.Features;

public sealed class SchemataSecurityFeature : FeatureBase
{
    public override int Priority => 300_100_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        services.TryAddScoped(typeof(IAccessProvider<,>), typeof(DefaultAccessProvider<,>));
        services.TryAddScoped(typeof(IEntitlementProvider<,>), typeof(DefaultEntitlementProvider<,>));
    }
}
