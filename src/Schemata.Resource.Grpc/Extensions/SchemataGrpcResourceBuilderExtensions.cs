using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Grpc;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Convenience overloads for registering gRPC-only resources with fewer type parameters.
/// </summary>
public static class SchemataGrpcResourceBuilderExtensions
{
    /// <summary>
    ///     Registers a gRPC-only resource using the entity type for all four type parameters.
    /// </summary>
    /// <typeparam name="TEntity">The entity type used as entity, request, detail, and summary.</typeparam>
    /// <param name="builder">The gRPC resource builder.</param>
    /// <returns>The gRPC resource builder for chaining.</returns>
    public static SchemataGrpcResourceBuilder Use<TEntity>(this SchemataGrpcResourceBuilder builder)
        where TEntity : class, ICanonicalName {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>();
    }

    /// <summary>
    ///     Registers a gRPC-only resource using the request type for detail and summary.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type, also used as detail and summary.</typeparam>
    /// <param name="builder">The gRPC resource builder.</param>
    /// <returns>The gRPC resource builder for chaining.</returns>
    public static SchemataGrpcResourceBuilder Use<TEntity, TRequest>(this SchemataGrpcResourceBuilder builder)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>();
    }

    /// <summary>
    ///     Registers a gRPC-only resource using the detail type as the summary type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TDetail">The detail type, also used as summary.</typeparam>
    /// <param name="builder">The gRPC resource builder.</param>
    /// <returns>The gRPC resource builder for chaining.</returns>
    public static SchemataGrpcResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataGrpcResourceBuilder builder)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>();
    }

    /// <summary>
    ///     Registers a gRPC-only resource with explicit entity, request, detail, and summary types.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TDetail">The detail type.</typeparam>
    /// <typeparam name="TSummary">The summary type.</typeparam>
    /// <param name="builder">The gRPC resource builder.</param>
    /// <returns>The gRPC resource builder for chaining.</returns>
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
