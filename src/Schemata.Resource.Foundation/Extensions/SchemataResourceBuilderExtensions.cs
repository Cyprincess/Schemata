using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataResourceBuilderExtensions
{
    public static SchemataResourceBuilder Use<TEntity>(this SchemataResourceBuilder builder)
        where TEntity : class, IIdentifier {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>();
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest>(this SchemataResourceBuilder builder)
        where TEntity : class, IIdentifier
        where TRequest : class, IIdentifier {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>();
    }

    public static SchemataResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataResourceBuilder builder)
        where TEntity : class, IIdentifier
        where TRequest : class, IIdentifier
        where TDetail : class, IIdentifier {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>();
    }
}
