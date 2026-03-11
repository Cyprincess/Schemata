using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataHttpResourceBuilderExtensions
{
    public static SchemataHttpResourceBuilder Use<TEntity>(
        this SchemataHttpResourceBuilder builder,
        string?                          package = null
    )
        where TEntity : class, ICanonicalName {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>(package);
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest>(
        this SchemataHttpResourceBuilder builder,
        string?                          package = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>(package);
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest, TDetail>(
        this SchemataHttpResourceBuilder builder,
        string?                          package = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>(package);
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(
        this SchemataHttpResourceBuilder builder,
        string?                          package = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        builder.Builder.Use<TEntity, TRequest, TDetail, TSummary>(package, [HttpResourceAttribute.Name]);

        return builder;
    }
}
