using System;
using System.Globalization;
using Schemata.Abstractions;
using Schemata.Entity.Cache;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

/// <summary>
///     Extension methods for <see cref="QueryContext{TEntity,TResult,T}" /> providing cache key
///     generation from provider key fragments.
/// </summary>
public static class QueryContextExtensions
{
    /// <summary>
    ///     Generates a cache key from the repository's provider key fragment
    ///     (<see cref="IQueryCacheKeyProvider" />) combined with the query's client-side
    ///     structure, the terminal operation, and the result type. Returns
    ///     <see langword="null" /> when the repository has no provider key, when the provider
    ///     cannot key the query, or when the structure cannot be represented safely; the cache
    ///     advisors then skip both read and fill.
    /// </summary>
    /// <typeparam name="TEntity">The root entity type being queried.</typeparam>
    /// <typeparam name="TResult">The projected result type of the query.</typeparam>
    /// <typeparam name="T">The scalar or aggregate return type.</typeparam>
    /// <param name="context">The query context to derive the cache key from.</param>
    /// <returns>A cache key string or <see langword="null" />.</returns>
    public static string? ToCacheKey<TEntity, TResult, T>(this QueryContext<TEntity, TResult, T> context)
        where TEntity : class {
        if (context.Repository is not IQueryCacheKeyProvider provider) {
            return null;
        }

        var providerKey = provider.GetQueryCacheKey(context.Query);
        var structure   = Stringizing.ToStructure(context.Query.Expression);

        if (string.IsNullOrWhiteSpace(providerKey) || string.IsNullOrWhiteSpace(structure)) {
            return null;
        }

        // The length prefixes keep the parts unambiguous when the fragments themselves contain
        // separator characters.
        var combined = $"{providerKey.Length.ToString(CultureInfo.InvariantCulture)}:{providerKey}"
                     + $"\x1e{structure.Length.ToString(CultureInfo.InvariantCulture)}:{structure}"
                     + $"\x1e{context.Operation}"
                     + $"\x1e{typeof(T).FullName}";

        return combined.ToCacheKey(SchemataConstants.Keys.Entity);
    }
}
