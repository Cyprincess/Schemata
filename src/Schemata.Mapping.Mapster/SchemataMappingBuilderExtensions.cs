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

/// <summary>
///     Extension methods for selecting Mapster as the mapping engine on <see cref="SchemataMappingBuilder" />.
/// </summary>
public static class SchemataMappingBuilderExtensions
{
    /// <summary>
    ///     Configures Mapster as the mapping engine, registering <see cref="TypeAdapterConfig" /> as a singleton.
    /// </summary>
    /// <param name="builder">The mapping builder.</param>
    /// <returns>The mapping builder for chaining.</returns>
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
