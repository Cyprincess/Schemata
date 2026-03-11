using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Grpc;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

public static class SchemataGrpcResourceBuilderExtensions
{
    public static SchemataGrpcResourceBuilder Use<TEntity>(this SchemataGrpcResourceBuilder builder)
        where TEntity : class, ICanonicalName {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>();
    }

    public static SchemataGrpcResourceBuilder Use<TEntity, TRequest>(this SchemataGrpcResourceBuilder builder)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>();
    }

    public static SchemataGrpcResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataGrpcResourceBuilder builder)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>();
    }

    public static SchemataGrpcResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(
        this SchemataGrpcResourceBuilder builder
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        builder.Builder.Use<TEntity, TRequest, TDetail, TSummary>([GrpcResourceAttribute.Name]);

        return builder;
    }
}
