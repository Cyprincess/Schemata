using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares a resource API with separate entity, request, detail, and summary types.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
/// <typeparam name="TSummary">The summary DTO type.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity, TRequest, TDetail, TSummary> : ResourceAttribute
{
    /// <inheritdoc />
    public ResourceAttribute() : base(typeof(TEntity), typeof(TRequest), typeof(TDetail), typeof(TSummary)) { }
}
