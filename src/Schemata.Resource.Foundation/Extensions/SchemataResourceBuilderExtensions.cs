using System;
using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Convenience overloads for resource registration with fewer type parameters. Each accepts an
///     optional transport selector that restricts the resource to specific endpoints.
/// </summary>
public static class SchemataResourceBuilderExtensions
{
    /// <summary>
    ///     Registers a resource using the entity type for all four type parameters.
    /// </summary>
    /// <typeparam name="TEntity">The entity type used for entity, request, detail, and summary.</typeparam>
    /// <param name="builder">The <see cref="SchemataResourceBuilder" />.</param>
    /// <param name="transports">
    ///     An optional callback selecting the transports that expose this resource; when omitted the
    ///     resource is exposed on every active transport.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataResourceBuilder Use<TEntity>(
        this SchemataResourceBuilder       builder,
        Action<ResourceEndpointSelector>?  transports = null
    )
        where TEntity : class, ICanonicalName {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>(transports);
    }

    /// <summary>
    ///     Registers a resource using the request type for detail and summary.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type, also used as detail and summary.</typeparam>
    /// <param name="builder">The <see cref="SchemataResourceBuilder" />.</param>
    /// <param name="transports">
    ///     An optional callback selecting the transports that expose this resource; when omitted the
    ///     resource is exposed on every active transport.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataResourceBuilder Use<TEntity, TRequest>(
        this SchemataResourceBuilder       builder,
        Action<ResourceEndpointSelector>?  transports = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>(transports);
    }

    /// <summary>
    ///     Registers a resource using the detail type as the summary type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TDetail">The detail type, also used as summary.</typeparam>
    /// <param name="builder">The <see cref="SchemataResourceBuilder" />.</param>
    /// <param name="transports">
    ///     An optional callback selecting the transports that expose this resource; when omitted the
    ///     resource is exposed on every active transport.
    /// </param>
    /// <returns>The builder for chaining.</returns>
    public static SchemataResourceBuilder Use<TEntity, TRequest, TDetail>(
        this SchemataResourceBuilder       builder,
        Action<ResourceEndpointSelector>?  transports = null
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>(transports);
    }
}
