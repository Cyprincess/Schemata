using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares a resource API with separate entity, request, and detail types.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TDetail">The detail DTO type.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity, TRequest, TDetail> : ResourceAttribute<TEntity, TRequest, TDetail, TDetail>;
