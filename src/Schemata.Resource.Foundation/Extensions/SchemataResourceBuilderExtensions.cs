using Schemata.Resource.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataResourceBuilderExtensions
{
    public static SchemataResourceBuilder Use<TEntity>(this SchemataResourceBuilder builder) {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>();
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest>(this SchemataResourceBuilder builder) {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>();
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataResourceBuilder builder) {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>();
    }
}
