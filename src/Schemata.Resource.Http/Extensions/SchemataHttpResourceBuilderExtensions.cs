using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataHttpResourceBuilderExtensions
{
    public static SchemataHttpResourceBuilder Use<TEntity>(this SchemataHttpResourceBuilder builder)
        where TEntity : class, IIdentifier {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>();
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest>(this SchemataHttpResourceBuilder builder)
        where TEntity : class, IIdentifier
        where TRequest : class, IIdentifier {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>();
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataHttpResourceBuilder builder)
        where TEntity : class, IIdentifier
        where TRequest : class, IIdentifier
        where TDetail : class, IIdentifier {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>();
    }

    public static SchemataHttpResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(this SchemataHttpResourceBuilder builder)
        where TEntity : class, IIdentifier
        where TRequest : class, IIdentifier
        where TDetail : class, IIdentifier
        where TSummary : class, IIdentifier {
        builder.Builder.Use<TEntity, TRequest, TDetail, TSummary>([new HttpResourceAttribute()]);
        return builder;
    }
}
