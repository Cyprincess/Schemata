using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Schemata.Core;
using Schemata.Core.Features;
using Schemata.Mapping.Skeleton;

namespace Schemata.Mapping.Foundation.Features;

public sealed class SchemataMappingFeature<T> : SchemataMappingFeature where T : class, ISimpleMapper
{
    public override int Priority => 340_000_000;

    public override void ConfigureServices(
        IServiceCollection  services,
        SchemataOptions     schemata,
        Configurators       configurators,
        IConfiguration      configuration,
        IWebHostEnvironment environment) {
        services.TryAddScoped<ISimpleMapper, T>();
    }
}

public abstract class SchemataMappingFeature : FeatureBase;
