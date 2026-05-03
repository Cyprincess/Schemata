using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

/// <inheritdoc cref="ResourceAttribute{TEntity,TRequest,TDetail,TSummary}" />
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute : Attribute
{
    /// <summary>
    ///     Maps the four type roles that together define a resource API surface.
    ///     Each parameter defaults to <paramref name="entity" /> so callers only
    ///     specify types that differ from the entity.
    /// </summary>
    /// <param name="entity">The persistence entity.</param>
    /// <param name="request">The DTO for create/update operations; defaults to <paramref name="entity" />.</param>
    /// <param name="detail">The DTO for single-resource read responses; defaults to <paramref name="entity" />.</param>
    /// <param name="summary">
    ///     The DTO for list-item responses; defaults to <paramref name="detail" /> or
    ///     <paramref name="entity" />.
    /// </param>
    public ResourceAttribute(
        Type  entity,
        Type? request = null,
        Type? detail  = null,
        Type? summary = null
    ) {
        Entity  = entity;
        Request = request ?? entity;
        Detail  = detail ?? entity;
        Summary = summary ?? detail ?? entity;
    }

    /// <summary>
    ///     The persistence entity type.
    /// </summary>
    public Type Entity { get; }

    /// <summary>
    ///     The type used for create and update request bodies.
    /// </summary>
    public Type? Request { get; }

    /// <summary>
    ///     The type used for single-resource read responses.
    /// </summary>
    public Type? Detail { get; }

    /// <summary>
    ///     The type used for each item in a list response.
    /// </summary>
    public Type? Summary { get; }

    /// <summary>
    ///     When set, restricts which endpoint types expose this resource.
    ///     If <see langword="null" />, all registered endpoints generate routes.
    /// </summary>
    public IList<string>? Endpoints { get; set; }
}
