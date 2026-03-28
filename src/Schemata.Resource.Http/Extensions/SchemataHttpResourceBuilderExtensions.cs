using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;
using Schemata.Resource.Http;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder;

/// <summary>
///     Convenience overloads for registering HTTP-only resources with fewer type parameters.
/// </summary>
public static class SchemataHttpResourceBuilderExtensions
{
    /// <summary>
    ///     Registers an HTTP-only resource using the entity type for all four type parameters.
    /// </summary>
    /// <typeparam name="TEntity">The entity type used as entity, request, detail, and summary.</typeparam>
    /// <param name="builder">The HTTP resource builder.</param>
    /// <returns>The HTTP resource builder for chaining.</returns>
    public static SchemataHttpResourceBuilder Use<TEntity>(this SchemataHttpResourceBuilder builder)
        where TEntity : class, ICanonicalName {
        return builder.Use<TEntity, TEntity, TEntity, TEntity>();
    }

    /// <summary>
    ///     Registers an HTTP-only resource using the request type for detail and summary.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type, also used as detail and summary.</typeparam>
    /// <param name="builder">The HTTP resource builder.</param>
    /// <returns>The HTTP resource builder for chaining.</returns>
    public static SchemataHttpResourceBuilder Use<TEntity, TRequest>(this SchemataHttpResourceBuilder builder)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TRequest, TRequest>();
    }

    /// <summary>
    ///     Registers an HTTP-only resource using the detail type as the summary type.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TDetail">The detail type, also used as summary.</typeparam>
    /// <param name="builder">The HTTP resource builder.</param>
    /// <returns>The HTTP resource builder for chaining.</returns>
    public static SchemataHttpResourceBuilder Use<TEntity, TRequest, TDetail>(this SchemataHttpResourceBuilder builder)
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName {
        return builder.Use<TEntity, TRequest, TDetail, TDetail>();
    }

    /// <summary>
    ///     Registers an HTTP-only resource with explicit entity, request, detail, and summary types.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TDetail">The detail type.</typeparam>
    /// <typeparam name="TSummary">The summary type.</typeparam>
    /// <param name="builder">The HTTP resource builder.</param>
    /// <returns>The HTTP resource builder for chaining.</returns>
    public static SchemataHttpResourceBuilder Use<TEntity, TRequest, TDetail, TSummary>(
        this SchemataHttpResourceBuilder builder
    )
        where TEntity : class, ICanonicalName
        where TRequest : class, ICanonicalName
        where TDetail : class, ICanonicalName
        where TSummary : class, ICanonicalName {
        builder.Builder.Use<TEntity, TRequest, TDetail, TSummary>([HttpResourceAttribute.Name]);

        return builder;
    }
}
