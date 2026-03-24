using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Grammars;
using Schemata.Resource.Foundation.Grammars.Expressions;
using Schemata.Resource.Foundation.Models;

namespace Schemata.Resource.Foundation;

/// <summary>
/// Accumulates query modifications (filtering, ordering, pagination, and arbitrary predicates) for a list request.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
public class ResourceRequestContainer<T>
{
    /// <summary>
    /// Gets or sets an optional callback to customize the filter grammar container before expression building.
    /// </summary>
    public Action<Container>? FilterConfigure { get; set; }

    /// <summary>
    /// Gets the composed query function that applies all accumulated modifications.
    /// </summary>
    public Func<IQueryable<T>, IQueryable<T>> Query { get; private set; } = q => q;

    /// <summary>
    /// Applies a parsed filter expression to the query pipeline.
    /// </summary>
    /// <param name="filter">The parsed filter to apply.</param>
    public void ApplyFiltering(Filter? filter) { Query = Query.WithFiltering(filter, FilterConfigure); }

    /// <summary>
    /// Applies ordering specifications to the query pipeline.
    /// </summary>
    /// <param name="order">The member-to-ordering mapping.</param>
    public void ApplyOrdering(Dictionary<Member, Ordering>? order) { Query = Query.WithOrdering(order); }

    /// <summary>
    /// Applies pagination (skip/take) from the page token to the query pipeline.
    /// </summary>
    /// <param name="token">The page token containing skip and page size values.</param>
    public void ApplyPaginating(PageToken token) { Query = Query.WithPaginating(token); }

    /// <summary>
    /// Applies an arbitrary predicate (e.g. parent scoping or entitlement filter) to the query pipeline.
    /// </summary>
    /// <param name="predicate">The predicate expression to add as a <see cref="System.Linq.Queryable.Where{TSource}(IQueryable{TSource}, System.Linq.Expressions.Expression{Func{TSource, bool}})">Where</see> clause.</param>
    public void ApplyModification(Expression<Func<T, bool>> predicate) {
        var query = Query;
        Query = q => query(q).Where(predicate);
    }
}
