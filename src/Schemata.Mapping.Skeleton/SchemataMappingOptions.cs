using System;
using System.Collections.Generic;
using Schemata.Mapping.Skeleton.Configurations;

namespace Schemata.Mapping.Skeleton;

public sealed class SchemataMappingOptions
{
    public List<IMapping> Mappings { get; } = [];

    public void AddMapping<TSource, TDestination>(Action<Map<TSource, TDestination>>? configure = null) {
        var map = new Map<TSource, TDestination>();

        configure?.Invoke(map);

        var mappings = map.Compile();

        Mappings.AddRange(mappings);
    }
}
