using System;
using System.Collections.Immutable;
using System.Linq.Expressions;

namespace Schemata.Mapping.Skeleton.Configurations;

public sealed class FieldSelection<TSource, TDestination>
{
    private readonly Mapping<TSource, TDestination> _mapping;

    internal FieldSelection(Mapping<TSource, TDestination> mapping) {
        _mapping = mapping;
    }

    public FieldSelection<TSource, TDestination> From(Expression<Func<TSource, object?>> sourceField) {
        _mapping.SetSourceField(sourceField);
        return new(_mapping);
    }

    public FieldSelection<TSource, TDestination> Ignore(
        Expression<Func<TSource, TDestination, bool>>? condition = null) {
        if (condition is null) {
            _mapping.SetIgnored();
            return new(_mapping);
        }

        _mapping.SetIgnoreCondition(condition);
        return new(_mapping);
    }

    public FieldSelection<TSource, TDestination> For(Expression<Func<TDestination, object?>> destinationField) {
        var mapping = _mapping.Map.Add(new(_mapping.Map, destinationField));
        return new(mapping);
    }

    internal ImmutableArray<IMapping> Compile() {
        return _mapping.Map.Compile();
    }
}
