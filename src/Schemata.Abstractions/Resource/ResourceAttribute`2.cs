using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares a resource API with separate entity and request types.
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity, TRequest> : ResourceAttribute<TEntity, TRequest, TEntity>;
