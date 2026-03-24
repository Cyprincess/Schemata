using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Schemata.Mapping.Skeleton.Configurations;

/// <summary>
/// Fluent entry point for defining field mappings and converters between a source and destination type.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TDestination">The destination type.</typeparam>
public sealed class Map<TSource, TDestination>
{
    private readonly List<IMapping> _mappings = [];

    internal Mapping<TSource, TDestination> Add(Mapping<TSource, TDestination> mapping) {
        _mappings.Add(mapping);
        return mapping;
    }

    internal void Remove(IMapping mapping) { _mappings.Remove(mapping); }

    /// <summary>
    /// Begins configuring a mapping for the specified destination field.
    /// </summary>
    /// <param name="destinationField">An expression selecting the destination property.</param>
    /// <returns>A <see cref="FieldSelection{TSource,TDestination}"/> for further configuration.</returns>
    public FieldSelection<TSource, TDestination> For(Expression<Func<TDestination, object?>> destinationField) {
        var mapping = Add(new(this, destinationField));
        return new(mapping);
    }

    /// <summary>
    /// Registers a whole-object converter expression instead of field-by-field mapping.
    /// </summary>
    /// <param name="with">An expression that converts the source to the destination.</param>
    /// <returns>This map for chaining.</returns>
    public Map<TSource, TDestination> With(Expression<Func<TSource, TDestination>> with) {
        var mapping = Add(new(this));
        mapping.SetWithExpression(with);
        return this;
    }

    internal IEnumerable<IMapping> Compile() {
        foreach (var mapping in _mappings) {
            if (mapping.IsConverter) {
                continue;
            }

            if (mapping.IsIgnored) {
                continue;
            }

            if (!mapping.HasSourceField) {
                throw new InvalidOperationException($"Mapping for field {
                    mapping.DestinationType
                } is missing a source field.");
            }
        }

        return _mappings;
    }
}
