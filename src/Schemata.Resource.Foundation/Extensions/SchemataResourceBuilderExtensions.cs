using Schemata.Resource.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataResourceBuilderExtensions
{
    public static SchemataResourceBuilder Use<TEntity>(this SchemataResourceBuilder builder) {
        return builder.Use(typeof(TEntity));
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest>(this SchemataResourceBuilder builder) {
        return builder.Use(typeof(TEntity), typeof(TRequest));
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataResourceBuilder builder) {
        return builder.Use(typeof(TEntity), typeof(TRequest), typeof(TDetail));
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(this SchemataResourceBuilder builder) {
        return builder.Use(typeof(TEntity), typeof(TRequest), typeof(TDetail), typeof(TSummary));
    }
}
