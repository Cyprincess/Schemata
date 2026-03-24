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
/// Extension methods for composing queryable filtering, ordering, and pagination operations.
/// </summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Composes a filter expression onto an existing query function.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The existing query function to extend.</param>
    /// <param name="filter">The parsed filter expression.</param>
    /// <param name="configure">Optional callback to customize the filter container.</param>
    /// <returns>A new query function that applies the filter.</returns>
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
    /// Composes ordering specifications onto an existing query function.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The existing query function to extend.</param>
    /// <param name="order">The member-to-ordering mapping.</param>
    /// <returns>A new query function that applies the ordering.</returns>
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
    /// Composes skip/take pagination onto an existing query function.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The existing query function to extend.</param>
    /// <param name="token">The page token containing skip and page size.</param>
    /// <returns>A new query function that applies pagination.</returns>
    public static Func<IQueryable<T>, IQueryable<T>> WithPaginating<T>(
        this Func<IQueryable<T>, IQueryable<T>> query,
        PageToken                               token
    ) {
        var build = query;

        query = q => build(q).Skip(token.Skip).Take(token.PageSize);

        return query;
    }

    /// <summary>
    /// Applies an ordering expression, chaining as <see cref="System.Linq.Queryable.ThenBy{TSource,TKey}(IOrderedQueryable{TSource}, System.Linq.Expressions.Expression{Func{TSource,TKey}})">ThenBy</see> when the source is already ordered.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="source">The queryable source.</param>
    /// <param name="select">The property selector expression.</param>
    /// <param name="ordering">The sort direction.</param>
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
