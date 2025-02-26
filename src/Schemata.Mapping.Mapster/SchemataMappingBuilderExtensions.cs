using Mapster;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Schemata.Core;
using Schemata.Mapping.Foundation;
using Schemata.Mapping.Foundation.Features;
using Schemata.Mapping.Mapster;
using Schemata.Mapping.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataMappingBuilderExtensions
{
    public static SchemataMappingBuilder UseMapster(this SchemataMappingBuilder builder) {
        builder.Services.TryAddSingleton(sp => {
            var options = sp.GetRequiredService<IOptions<SchemataMappingOptions>>();

            var config = TypeAdapterConfig.GlobalSettings.Clone();

            config.Default
                  .IgnoreNullValues(true)
                  .PreserveReference(true);

            MapsterConfigurator.Configure(config, options.Value);

            return config;
        });

        builder.Schemata.AddFeature<SchemataMappingFeature<SimpleMapper>>();

        return builder;
    }
}
