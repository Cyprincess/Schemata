using System;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Mapping.Foundation;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Skeleton.Configurations;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataMappingBuilderExtensions
{
    public static SchemataMappingBuilder Map<TSource, TDestination>(
        this SchemataMappingBuilder         builder,
        Action<Map<TSource, TDestination>>? configure = null) {
        builder.Services.Configure<SchemataMappingOptions>(options => {
            options.AddMapping(configure);
        });

        return builder;
    }
}
