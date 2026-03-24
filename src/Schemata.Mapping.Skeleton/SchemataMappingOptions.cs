using System;
using System.Collections.Generic;
using Schemata.Mapping.Skeleton.Configurations;

namespace Schemata.Mapping.Skeleton;

/// <summary>
/// Accumulates mapping configurations that are applied when the mapping engine is initialized.
/// </summary>
public sealed class SchemataMappingOptions
{
    /// <summary>
    /// The collection of compiled mapping definitions.
    /// </summary>
    public List<IMapping> Mappings { get; } = [];

    /// <summary>
    /// Adds a mapping configuration for the specified source and destination types.
    /// </summary>
    /// <typeparam name="TSource">The source type.</typeparam>
    /// <typeparam name="TDestination">The destination type.</typeparam>
    /// <param name="configure">An optional action to configure field mappings, converters, and ignores.</param>
    public void AddMapping<TSource, TDestination>(Action<Map<TSource, TDestination>>? configure = null) {
        var map = new Map<TSource, TDestination>();

        configure?.Invoke(map);

        var mappings = map.Compile();

        Mappings.AddRange(mappings);
    }
}
