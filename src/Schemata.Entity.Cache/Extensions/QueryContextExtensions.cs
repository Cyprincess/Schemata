using System;
using Schemata.Entity.Cache;

// ReSharper disable once CheckNamespace
namespace Schemata.Entity.Repository;

public static class QueryContextExtensions
{
    public static string? ToCacheKey<TEntity, TResult, T>(this QueryContext<TEntity, TResult, T> context)
        where TEntity : class {
        var query = Stringizing.ToString(context.Query.Expression);
        return !string.IsNullOrWhiteSpace(query) ? string.Concat(query, "\x1e", typeof(T).Name).ToCacheKey() : null;
    }
}
