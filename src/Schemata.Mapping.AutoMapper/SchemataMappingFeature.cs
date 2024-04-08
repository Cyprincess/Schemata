using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Mapping.Skeleton;

namespace Schemata.Mapping.AutoMapper;

public class SchemataMappingFeature : FeatureBase
{
    public override int Priority => 330_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        services.TryAddSingleton<ISimpleMapper, SimpleMapper>();
    }
}
