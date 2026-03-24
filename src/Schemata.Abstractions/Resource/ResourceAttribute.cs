using System;
using System.Collections.Generic;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares the entity, request, detail, and summary types that make up a resource API.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ResourceAttribute" /> class.
    /// </summary>
    /// <param name="entity">The entity type.</param>
    /// <param name="request">The request DTO type; defaults to <paramref name="entity" />.</param>
    /// <param name="detail">The detail DTO type; defaults to <paramref name="entity" />.</param>
    /// <param name="summary">The summary DTO type; defaults to <paramref name="detail" /> or <paramref name="entity" />.</param>
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
    ///     Gets the entity type.
    /// </summary>
    public Type Entity { get; }

    /// <summary>
    ///     Gets the request DTO type.
    /// </summary>
    public Type? Request { get; }

    /// <summary>
    ///     Gets the detail DTO type.
    /// </summary>
    public Type? Detail { get; }

    /// <summary>
    ///     Gets the summary DTO type.
    /// </summary>
    public Type? Summary { get; }

    /// <summary>
    ///     Gets or sets the list of endpoint types this resource supports (e.g., "HTTP", "gRPC").
    /// </summary>
    public IList<string>? Endpoints { get; set; }
}
