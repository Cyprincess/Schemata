using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Grpc;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataGrpcResourceBuilderExtensions
{
    public static SchemataGrpcResourceBuilder Use<TEntity>(
        this SchemataGrpcResourceBuilder builder,
        string?                          package = null
    )
        where TEntity : class, ICanonicalName {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>(package);
    }

    public static SchemataGrpcResourceBuilder Use<TEntity, TRequest>(
        this SchemataGrpcResourceBuilder builder,
        string?                          package = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>(package);
    }

    public static SchemataGrpcResourceBuilder Use<TEntity, TRequest, TDetail>(
        this SchemataGrpcResourceBuilder builder,
        string?                          package = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>(package);
    }

    public static SchemataGrpcResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(
        this SchemataGrpcResourceBuilder builder,
        string?                          package = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        builder.Builder.Use<TEntity, TRequest, TDetail, TSummary>(package, [GrpcResourceAttribute.Name]);

        return builder;
    }
}
