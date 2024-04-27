using System;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

public static class QueryContextExtensions
{
    public static string? ToCacheKey<TEntity, TResult, T>(this QueryContext<TEntity, TResult, T> context)
        where TEntity : class {
        var query = context.Repository.GetQueryString(context.Query);
        return !string.IsNullOrWhiteSpace(query)
            ? string.Concat(query, "\x1e", typeof(T).Name).ToCacheKey()
            : null;
    }
}
