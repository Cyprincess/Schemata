using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using AutoMapper;
using Schemata.Mapping.Skeleton;
using Schemata.Mapping.Skeleton.Configurations;

namespace Schemata.Mapping.AutoMapper;

public static class AutoMapperConfigurator
{
    public static IMapperConfigurationExpression Configure(
        IMapperConfigurationExpression config,
        SchemataMappingOptions         options) {
        var method = typeof(AutoMapperConfigurator).GetMethod(nameof(Map), BindingFlags.Static | BindingFlags.NonPublic);
        foreach (var group in options.Mappings.GroupBy(m => (m.SourceType, m.DestinationType))) {
            var invoke = method!.MakeGenericMethod(group.Key.SourceType, group.Key.DestinationType);
            invoke.Invoke(null, [config, group]);
        }

        return config;
    }

    private static void Map<TSource, TDestination>(
        IMapperConfigurationExpression config,
        IEnumerable<IMapping>          mappings) {
        var setter = config.CreateMap<TSource, TDestination>();

        foreach (var mapping in mappings) {
            mapping.Invoke((
                with,
                destination,
                source,
                condition,
                ignored) => {
                if (with is Expression<Func<TSource, TDestination>> converter) {
                    setter.ConstructUsing(converter);
                    return;
                }

                if (destination is not Expression<Func<TDestination, object>> member) {
                    return;
                }

                if (ignored) {
                    setter.ForMember(member, options => {
                        options.Ignore();
                    });
                    return;
                }

                if (source is not Expression<Func<TSource, object>> expression) {
                    return;
                }

                setter.ForMember(member, options => {
                    options.MapFrom(expression);

                    if (condition is not Expression<Func<TSource, TDestination, bool>> predicate) {
                        return;
                    }

                    var negative = predicate.Compile();
                    options.Condition((s, d) => !negative(s, d));
                });
            });
        }
    }
}
