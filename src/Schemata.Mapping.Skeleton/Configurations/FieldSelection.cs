using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Schemata.Mapping.Skeleton.Configurations;

/// <summary>
/// Fluent builder for configuring an individual field mapping within a <see cref="Map{TSource,TDestination}"/>.
/// </summary>
/// <typeparam name="TSource">The source type.</typeparam>
/// <typeparam name="TDestination">The destination type.</typeparam>
public sealed class FieldSelection<TSource, TDestination>
{
    private readonly Mapping<TSource, TDestination> _mapping;

    internal FieldSelection(Mapping<TSource, TDestination> mapping) { _mapping = mapping; }

    /// <summary>
    /// Specifies the source field expression for this mapping.
    /// </summary>
    /// <param name="sourceField">An expression selecting the source property.</param>
    /// <returns>This field selection for chaining.</returns>
    public FieldSelection<TSource, TDestination> From(Expression<Func<TSource, object?>> sourceField) {
        _mapping.SetSourceField(sourceField);
        return new(_mapping);
    }

    /// <summary>
    /// Marks this destination field as ignored, optionally based on a condition.
    /// </summary>
    /// <param name="condition">An optional predicate; when it returns <see langword="true"/>, the field is ignored.</param>
    /// <returns>This field selection for chaining.</returns>
    public FieldSelection<TSource, TDestination> Ignore(
        Expression<Func<TSource, TDestination, bool>>? condition = null
    ) {
        if (condition is null) {
            _mapping.SetIgnored();
            return new(_mapping);
        }

        _mapping.SetIgnoreCondition(condition);
        return new(_mapping);
    }

    /// <summary>
    /// Begins configuring a new destination field mapping.
    /// </summary>
    /// <param name="destinationField">An expression selecting the destination property.</param>
    /// <returns>A new field selection for the specified destination field.</returns>
    public FieldSelection<TSource, TDestination> For(Expression<Func<TDestination, object?>> destinationField) {
        var mapping = _mapping.Map.Add(new(_mapping.Map, destinationField));
        return new(mapping);
    }

    internal IEnumerable<IMapping> Compile() { return _mapping.Map.Compile(); }
}
