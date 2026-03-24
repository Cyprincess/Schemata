using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Mapping.Foundation;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Skeleton.Configurations;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Extension methods for registering mappings on <see cref="SchemataMappingBuilder"/>.
/// </summary>
public static class SchemataMappingBuilderExtensions
{
    /// <summary>
    /// Registers a mapping configuration between the specified source and destination types.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="builder">The mapping builder.</param>
    /// <param name="configure">An optional action to configure field mappings.</param>
    /// <returns>The mapping builder for chaining.</returns>
    public static SchemataMappingBuilder Map<TSource, TDestination>(
        this SchemataMappingBuilder         builder,
        Action<Map<TSource, TDestination>>? configure = null
    ) {
        builder.Services.Configure<SchemataMappingOptions>(options => { options.AddMapping(configure); });

        return builder;
    }
}
