using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Convenience overload per
///     <seealso href="https://google.aip.dev/121">AIP-121: Resource-oriented design</seealso>
///     and <seealso href="https://google.aip.dev/123">AIP-123: Resource types</seealso>
///     separating the request DTO from the entity, while detail and summary
///     both default to the entity type.
/// </summary>
/// <typeparam name="TEntity">The persistence entity.</typeparam>
/// <typeparam name="TRequest">The DTO for create and update requests.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity, TRequest> : ResourceAttribute<TEntity, TRequest, TEntity>;
