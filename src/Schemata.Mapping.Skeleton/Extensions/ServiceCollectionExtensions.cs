using System;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Skeleton.Configurations;

// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection Map<TSource, TDestination>(
        this IServiceCollection             services,
        Action<Map<TSource, TDestination>>? configure = null) {
        services.Configure<SchemataMappingOptions>(options => {
            options.AddMapping(configure);
        });

        return services;
    }
}
