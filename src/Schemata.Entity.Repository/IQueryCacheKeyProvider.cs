using System.Linq;

namespace Schemata.Entity.Repository;

/// <summary>
///     Optional repository capability producing a stable cache-key fragment for a queryable
///     from the command the provider would actually execute. Implemented by concrete
///     repository providers; the query cache consults it when the repository implements it.
/// </summary>
public interface IQueryCacheKeyProvider
{
    /// <summary>
    ///     Returns a stable key fragment identifying the data source, command text, and ordered
    ///     typed parameters the query would execute, or <see langword="null" /> when the query
    ///     cannot be reduced to a stable command.
    /// </summary>
    /// <remarks>
    ///     Returning <see langword="null" /> disables caching for the query: neither the cached
    ///     read nor the result fill participates. The fragment must be hash-safe — raw
    ///     connection identity, command text, and parameter values may be inputs but should not
    ///     survive into the returned value. Prefer composing the fragment with
    ///     <see cref="QueryCacheKey.Create" />, which hashes internally.
    /// </remarks>
    /// <typeparam name="TResult">The projected result type of the query.</typeparam>
    /// <param name="query">
    ///     The queryable after all build-query advisors and the user predicate have been applied.
    /// </param>
    /// <returns>The key fragment, or <see langword="null" /> when the query is not cacheable.</returns>
    string? GetQueryCacheKey<TResult>(IQueryable<TResult> query);
}
