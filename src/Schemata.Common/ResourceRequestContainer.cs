using System;
using System.Linq;
using System.Linq.Expressions;

namespace Schemata.Common;

/// <summary>Accumulates composable filtering, ordering, and pagination for a resource query.</summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
public class ResourceRequestContainer<T>
{
    /// <summary>Gets the composed query function that applies all accumulated modifications.</summary>
    public Func<IQueryable<T>, IQueryable<T>> Query { get; private set; } = q => q;

    /// <summary>Appends a predicate to the composed query.</summary>
    public void ApplyWhere(Expression<Func<T, bool>>? predicate) {
        if (predicate is null) {
            return;
        }

        var query = Query;
        Query = q => query(q).Where(predicate);
    }

    /// <summary>Adds an ordering function to the composed query.</summary>
    public void ApplyOrdering(Func<IQueryable<T>, IOrderedQueryable<T>>? order) {
        if (order is null) {
            return;
        }

        var query = Query;
        Query = q => order(query(q));
    }

    /// <summary>Applies skip/take pagination from the supplied page values.</summary>
    public void ApplyPaginating(IPagination? pagination, int lookahead = 0) {
        Query = Query.WithPaginating(pagination, lookahead);
    }
}
