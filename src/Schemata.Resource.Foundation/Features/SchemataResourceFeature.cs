using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Resource.Foundation.Advices;

namespace Schemata.Resource.Foundation.Features;

[DependsOn<SchemataRoutingFeature>]
[Information("Resource Service depends on Routing feature, it will be added automatically.", Level = LogLevel.Debug)]
[Information("Resource Service depends on Mapping feature, you should add it manually.", Level = LogLevel.Information)]
public class SchemataResourceFeature : FeatureBase
{
    public override int Priority => 360_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceAddAdvice<,>), typeof(AdviceAddValidation<,>)));
        services.TryAddEnumerable(ServiceDescriptor.Scoped(typeof(IResourceEditAdvice<,>), typeof(AdviceEditValidation<,>)));
    }
}
