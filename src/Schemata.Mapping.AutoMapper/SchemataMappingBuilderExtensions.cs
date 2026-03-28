using AutoMapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Schemata.Core;
using Schemata.Mapping.AutoMapper;
using Schemata.Mapping.Foundation;
using Schemata.Mapping.Foundation.Features;
using Schemata.Mapping.Skeleton;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Extension methods for selecting AutoMapper as the mapping engine on <see cref="SchemataMappingBuilder" />.
/// </summary>
public static class SchemataMappingBuilderExtensions
{
    /// <summary>
    ///     Configures AutoMapper as the mapping engine, registering <see cref="MapperConfiguration" /> as a singleton.
    /// </summary>
    /// <param name="builder">The mapping builder.</param>
    /// <returns>The mapping builder for chaining.</returns>
    public static SchemataMappingBuilder UseAutoMapper(this SchemataMappingBuilder builder) {
        builder.Services.TryAddSingleton(sp => {
            var options = sp.GetRequiredService<IOptions<SchemataMappingOptions>>();
            var logging = sp.GetRequiredService<ILoggerFactory>();

            return new MapperConfiguration(mapper => { AutoMapperConfigurator.Configure(mapper, options.Value); }, logging);
        });

        builder.Schemata.AddFeature<SchemataMappingFeature<SimpleMapper>>();

        return builder;
    }
}
