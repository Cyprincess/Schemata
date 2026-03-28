using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Mapster;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Skeleton.Configurations;

namespace Schemata.Mapping.Mapster;

/// <summary>
///     Translates Schemata mapping configurations into Mapster type adapter rules.
/// </summary>
public static class MapsterConfigurator
{
    /// <summary>
    ///     Applies all <see cref="SchemataMappingOptions" /> mappings to the given Mapster configuration.
    /// </summary>
    /// <param name="config">The Mapster type adapter configuration.</param>
    /// <param name="options">The Schemata mapping options containing compiled mapping definitions.</param>
    /// <returns>The configuration for chaining.</returns>
    public static TypeAdapterConfig Configure(TypeAdapterConfig config, SchemataMappingOptions options) {
        var method = typeof(MapsterConfigurator).GetMethod(nameof(Map), BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var group in options.Mappings.GroupBy(m => (m.SourceType, m.DestinationType))) {
            var invoke = method!.MakeGenericMethod(group.Key.SourceType, group.Key.DestinationType);
            invoke.Invoke(null, [config, group]);
        }

        return config;
    }

    private static void Map<TSource, TDestination>(TypeAdapterConfig config, IEnumerable<IMapping> mappings) {
        var setter = config.NewConfig<TSource, TDestination>();

        foreach (var mapping in mappings) {
            mapping.Invoke((with, destination, source, condition, ignored) => {
                if (with is Expression<Func<TSource, TDestination>> converter) {
                    setter.MapWith(converter);
                    return;
                }

                if (destination is not Expression<Func<TDestination, object>> member) {
                    return;
                }

                if (ignored) {
                    setter.Ignore(member);
                    return;
                }

                if (source is not Expression<Func<TSource, object>> expression) {
                    return;
                }

                setter.Map(member, expression);

                if (condition is not Expression<Func<TSource, TDestination, bool>> predicate) {
                    return;
                }

                setter.IgnoreIf(predicate, member);
            });
        }
    }
}
