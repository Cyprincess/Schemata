using System;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Skeleton.Configurations;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Extension methods for registering mapping configurations on <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers a mapping configuration between the specified source and destination types.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional action to configure field mappings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection Map<TSource, TDestination>(
        this IServiceCollection             services,
        Action<Map<TSource, TDestination>>? configure = null
    ) {
        services.Configure<SchemataMappingOptions>(options => { options.AddMapping(configure); });

        return services;
    }
}
