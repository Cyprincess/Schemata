using Schemata.Resource.Foundation.Models;

// ReSharper disable once CheckNamespace
namespace System.Linq;

/// <summary>
///     Extension methods that compose filtering
///     per <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>, ordering, and pagination
///     per <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso> onto query functions.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    ///     Composes skip/take pagination onto the query pipeline.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The query function to extend.</param>
    /// <param name="token">The page token, or <see langword="null" />.</param>
    /// <param name="lookahead">
    ///     Extra rows fetched beyond the page size so the caller can detect a
    ///     following page without counting the collection.
    /// </param>
    /// <returns>The composed query function.</returns>
    public static Func<IQueryable<T>, IQueryable<T>> WithPaginating<T>(
        this Func<IQueryable<T>, IQueryable<T>> query,
        PageToken?                              token,
        int                                     lookahead = 0
    ) {
        if (token is null) {
            return query;
        }

        var build = query;

        query = q => build(q).Skip(token.Skip).Take(token.PageSize + lookahead);

        return query;
    }
}
