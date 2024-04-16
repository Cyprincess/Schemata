using System.Collections.Generic;
using System.Linq.Expressions;
using Humanizer;
using Schemata.Abstractions;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation.Grammars;
using Schemata.Resource.Foundation.Grammars.Terms;

// ReSharper disable once CheckNamespace
namespace System.Linq;

public static class QueryableExtensions
{
    public static Func<IQueryable<T>, IQueryable<T>> ApplyFiltering<T>(
        this Func<IQueryable<T>, IQueryable<T>> query,
        Filter?                                 filter) {
        if (filter is null) {
            return query;
        }

        var container = Container.Build(filter);
        BindProperties<T>(container);

        var expression = container.Build();

        var predicate = ToPredicate<T>(expression);
        if (predicate is null) {
            return query;
        }

        return q => query(q).Where(predicate);
    }

    public static Func<IQueryable<T>, IQueryable<T>> ApplyOrdering<T>(
        this Func<IQueryable<T>, IQueryable<T>> query,
        Dictionary<Member, Ordering>?           order) {
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

            query = q => build(q).ApplyOrdering(select, ordering);
        }

        return query;
    }

    public static IOrderedQueryable<T> ApplyOrdering<T>(
        this IQueryable<T>          source,
        Expression<Func<T, object>> select,
        Ordering                    ordering = Ordering.Ascending) {
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
