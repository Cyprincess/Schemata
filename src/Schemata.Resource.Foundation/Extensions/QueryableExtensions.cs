using System.Collections.Generic;
using System.Linq.Expressions;
using Humanizer;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Resource.Foundation.Grammars;
using Schemata.Resource.Foundation.Grammars.Expressions;
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
    ///     Composes a parsed filter expression onto the query pipeline.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The query function to extend.</param>
    /// <param name="filter">The parsed filter, or <see langword="null" />.</param>
    /// <param name="configure">Optional callback to customize the filter container.</param>
    /// <returns>The composed query function.</returns>
    public static Func<IQueryable<T>, IQueryable<T>> WithFiltering<T>(
        this Func<IQueryable<T>, IQueryable<T>> query,
        Filter?                                 filter,
        Action<Container>?                      configure = null
    ) {
        if (filter is null) {
            return query;
        }

        var container = Container.Build(filter);
        BindProperties<T>(container);
        configure?.Invoke(container);

        var expression = container.Build();

        var predicate = ToPredicate<T>(expression);
        if (predicate is null) {
            return query;
        }

        return q => query(q).Where(predicate);
    }

    /// <summary>
    ///     Composes ordering specifications onto the query pipeline.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The query function to extend.</param>
    /// <param name="order">The member-to-ordering mapping, or <see langword="null" />.</param>
    /// <returns>The composed query function.</returns>
    public static Func<IQueryable<T>, IQueryable<T>> WithOrdering<T>(
        this Func<IQueryable<T>, IQueryable<T>> query,
        Dictionary<Member, Ordering>?           order
    ) {
        if (order is not { Count: > 0 }) {
            return query;
        }

        foreach (var (member, ordering) in order) {
            var container = Container.Build(member);
            BindProperties<T>(container);

            var expression = container.Build();

            var select = ToSelect<T>(expression);
            if (select is null) {
                continue;
            }

            var build = query;

            query = q => build(q).WithOrdering(select, ordering);
        }

        return query;
    }

    /// <summary>
    ///     Composes skip/take pagination onto the query pipeline.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The query function to extend.</param>
    /// <param name="token">The page token, or <see langword="null" />.</param>
    /// <returns>The composed query function.</returns>
    public static Func<IQueryable<T>, IQueryable<T>> WithPaginating<T>(
        this Func<IQueryable<T>, IQueryable<T>> query,
        PageToken?                              token
    ) {
        if (token is null) {
            return query;
        }

        var build = query;

        query = q => build(q).Skip(token.Skip).Take(token.PageSize);

        return query;
    }

    /// <summary>
    ///     Applies an ordering expression. When the source is already ordered, chains
    ///     via <c>ThenBy</c>/<c>ThenByDescending</c>; otherwise uses
    ///     <c>OrderBy</c>/<c>OrderByDescending</c>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="select">The property selector expression.</param>
    /// <param name="ordering">The sort direction; defaults to <see cref="Ordering.Ascending" />.</param>
    /// <returns>An ordered queryable.</returns>
    public static IOrderedQueryable<T> WithOrdering<T>(
        this IQueryable<T>          source,
        Expression<Func<T, object>> select,
        Ordering                    ordering = Ordering.Ascending
    ) {
        if (typeof(IOrderedQueryable<T>).IsAssignableFrom(source.Expression.Type)) {
            return ordering == Ordering.Ascending
                ? ((IOrderedQueryable<T>)source).ThenBy(select)
                : ((IOrderedQueryable<T>)source).ThenByDescending(select);
        }

        return ordering == Ordering.Ascending ? source.OrderBy(select) : source.OrderByDescending(select);
    }

    private static void BindProperties<T>(Container container) {
        var type = typeof(T);
        var name = type.Name.Singularize().Underscore();

        container.Bind(name, type);

        container.TryGetParameter(name, out var e);

        var properties = AppDomainTypeCache.GetProperties(type);
        foreach (var (key, info) in properties) {
            var property = Expression.Property(e, info);
            container.Bind(key.Underscore(), property);
        }
    }

    private static Expression<Func<T, object>>? ToSelect<T>(LambdaExpression? lambda) {
        if (lambda is null) {
            return null;
        }

        return Expression.Lambda<Func<T, object>>(Expression.Convert(lambda.Body, typeof(object)), lambda.Parameters);
    }

    private static Expression<Func<T, bool>>? ToPredicate<T>(LambdaExpression? lambda) {
        if (lambda is null) {
            return null;
        }

        return Expression.Lambda<Func<T, bool>>(lambda.Body, lambda.Parameters);
    }
}
