using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Entity.Repository;

namespace Schemata.Resource.Foundation;

/// <summary>
///     Appends a deterministic key ordering to list queries so skip/take pagination
///     is stable per <seealso href="https://google.aip.dev/132">AIP-132</seealso> and
///     <seealso href="https://google.aip.dev/158">AIP-158</seealso>: providers return rows in an
///     unspecified order when no <c>ORDER BY</c> is present, drifting page boundaries
///     between calls. Key properties come from
///     <see cref="RepositoryBase.KeyPropertiesCache" /> (class-level primary key
///     attribute, then <see cref="IIdentifier.Uid" />), falling back to
///     <see cref="ICanonicalName.Name" />.
/// </summary>
/// <typeparam name="TEntity">The entity type being ordered.</typeparam>
internal static class KeyOrdering<TEntity>
    where TEntity : class, ICanonicalName
{
    private static readonly MethodInfo OrderByOpen = typeof(Queryable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Queryable.OrderBy) && m.GetParameters().Length == 2);

    private static readonly MethodInfo ThenByOpen = typeof(Queryable)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == nameof(Queryable.ThenBy) && m.GetParameters().Length == 2);

    private static readonly IReadOnlyList<(LambdaExpression Selector, Type KeyType)> Keys = Build();

    /// <summary>
    ///     Wraps the user ordering (or none) so the key ordering always applies last.
    /// </summary>
    /// <param name="order">The compiled <c>order_by</c> ordering, or <see langword="null" />.</param>
    /// <returns>An ordering function ending in the deterministic key ordering.</returns>
    public static Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>> Compose(
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? order
    ) {
        if (order is null) {
            return OrderByKeys;
        }

        return q => ThenByKeys(order(q));
    }

    private static IOrderedQueryable<TEntity> OrderByKeys(IQueryable<TEntity> query) {
        var ordered = Apply(query, OrderByOpen, Keys[0]);
        for (var i = 1; i < Keys.Count; i++) {
            ordered = Apply(ordered, ThenByOpen, Keys[i]);
        }

        return ordered;
    }

    private static IOrderedQueryable<TEntity> ThenByKeys(IOrderedQueryable<TEntity> query) {
        var ordered = query;
        foreach (var key in Keys) {
            ordered = Apply(ordered, ThenByOpen, key);
        }

        return ordered;
    }

    private static IOrderedQueryable<TEntity> Apply(
        IQueryable<TEntity>            query,
        MethodInfo                     open,
        (LambdaExpression Selector, Type KeyType) key
    ) {
        var method = open.MakeGenericMethod(typeof(TEntity), key.KeyType);
        return (IOrderedQueryable<TEntity>)method.Invoke(null, [query, key.Selector])!;
    }

    private static IReadOnlyList<(LambdaExpression, Type)> Build() {
        IEnumerable<PropertyInfo> properties = RepositoryBase.KeyPropertiesCache(typeof(TEntity));
        if (!properties.Any()) {
            properties = [
                AppDomainTypeCache.GetProperty(typeof(TEntity), nameof(ICanonicalName.Name))
             ?? AppDomainTypeCache.GetProperty(typeof(ICanonicalName), nameof(ICanonicalName.Name))!,
            ];
        }

        var keys = new List<(LambdaExpression, Type)>();
        foreach (var property in properties) {
            var parameter = Expression.Parameter(typeof(TEntity), "entity");
            var selector  = Expression.Lambda(Expression.Property(parameter, property), parameter);
            keys.Add((selector, property.PropertyType));
        }

        return keys;
    }
}
