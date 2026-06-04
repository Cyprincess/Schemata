using System;
using System.Linq;
using System.Linq.Expressions;
using Schemata.Resource.Foundation.Models;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Accumulates query modifications — filtering
///     per <seealso href="https://google.aip.dev/160">AIP-160: Filtering</seealso>, ordering,
///     pagination per <seealso href="https://google.aip.dev/158">AIP-158: Pagination</seealso>, and
///     custom predicates (parent scoping, entitlement filtering) — into a composable
///     query function used by <see cref="ResourceOperationHandler{TEntity,TRequest,TDetail,TSummary}" />.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
public class ResourceRequestContainer<T>
{
    /// <summary>
    ///     Gets the composed query function that applies all accumulated modifications.
    /// </summary>
    public Func<IQueryable<T>, IQueryable<T>> Query { get; private set; } = q => q;

    public void ApplyFiltering(Expression<Func<T, bool>>? predicate) {
        if (predicate is null) {
            return;
        }

        var query = Query;
        Query = q => query(q).Where(predicate);
    }

    public void ApplyOrdering(Func<IQueryable<T>, IOrderedQueryable<T>>? order) {
        if (order is null) {
            return;
        }

        var query = Query;
        Query = q => order(query(q));
    }

    /// <summary>
    ///     Applies skip/take pagination from the page token.
    /// </summary>
    /// <param name="token">The <see cref="PageToken" />.</param>
    public void ApplyPaginating(PageToken? token) { Query = Query.WithPaginating(token); }

    /// <summary>
    ///     Applies an arbitrary predicate (e.g. parent scoping or entitlement filter)
    ///     as a <c>Queryable.Where&lt;TSource&gt;</c> clause.
    /// </summary>
    /// <param name="predicate">The predicate expression, or <see langword="null" />.</param>
    public void ApplyModification(Expression<Func<T, bool>>? predicate) {
        if (predicate is null) {
            return;
        }

        var query = Query;
        Query = q => query(q).Where(predicate);
    }
}
