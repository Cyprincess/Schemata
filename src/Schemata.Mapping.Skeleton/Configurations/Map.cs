using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Schemata.Mapping.Skeleton.Configurations;

public sealed class Map<TSource, TDestination>
{
    private readonly List<IMapping> _mappings = [];

    internal Mapping<TSource, TDestination> Add(Mapping<TSource, TDestination> mapping) {
        _mappings.Add(mapping);
        return mapping;
    }

    internal void Remove(IMapping mapping) {
        _mappings.Remove(mapping);
    }

    public FieldSelection<TSource, TDestination> For(Expression<Func<TDestination, object?>> destinationField) {
        var mapping = Add(new(this, destinationField));
        return new(mapping);
    }

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
