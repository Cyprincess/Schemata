using Schemata.Abstractions.Entities;
using Schemata.Resource.Foundation;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
/// Convenience overloads for registering resources with fewer type parameters.
/// </summary>
public static class SchemataResourceBuilderExtensions
{
    /// <summary>
    /// Registers a resource using the entity type for all four type parameters.
    /// </summary>
    /// <typeparam name="TEntity">The entity type used as entity, request, detail, and summary.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static SchemataResourceBuilder Use<TEntity>(this SchemataResourceBuilder builder)
        where TEntity : class, ICanonicalName {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>();
    }

    /// <summary>
    /// Registers a resource using the request type for detail and summary.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type, also used as detail and summary.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static SchemataResourceBuilder Use<TEntity, TRequest>(this SchemataResourceBuilder builder)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>();
    }

    /// <summary>
    /// Registers a resource using the detail type as the summary type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TDetail">The detail type, also used as summary.</typeparam>
    /// <param name="builder">The resource builder.</param>
    /// <returns>The resource builder for chaining.</returns>
    public static SchemataResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataResourceBuilder builder)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>();
    }
}
