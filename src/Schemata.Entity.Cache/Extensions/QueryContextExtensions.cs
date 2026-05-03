using System;
using Schemata.Abstractions;
using Schemata.Entity.Cache;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

/// <summary>
///     Extension methods for <see cref="QueryContext{TEntity,TResult,T}" /> providing cache key generation
///     via <see cref="Stringizing" />.
/// </summary>
public static class QueryContextExtensions
{
    /// <summary>
    ///     Generates a cache key from the stringized query expression and return type, or
    ///     <see langword="null" /> if the expression cannot be stringized.
    /// </summary>
    /// <typeparam name="TEntity">The root entity type being queried.</typeparam>
    /// <typeparam name="TResult">The projected result type of the query.</typeparam>
    /// <typeparam name="T">The scalar or aggregate return type.</typeparam>
    /// <param name="context">The query context to derive the cache key from.</param>
    /// <returns>A cache key string or <see langword="null" />.</returns>
    public static string? ToCacheKey<TEntity, TResult, T>(this QueryContext<TEntity, TResult, T> context)
        where TEntity : class {
        var query = Stringizing.ToString(context.Query.Expression);
        return !string.IsNullOrWhiteSpace(query)
            ? $"{query}\x1e{typeof(T).Name}".ToCacheKey(SchemataConstants.Keys.Entity)
            : null;
    }
}
