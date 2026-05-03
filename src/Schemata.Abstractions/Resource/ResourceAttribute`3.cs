using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Convenience overload per
///     <seealso href="https://google.aip.dev/121">AIP-121: Resource-oriented design</seealso>
///     and <seealso href="https://google.aip.dev/123">AIP-123: Resource types</seealso>
///     separating entity, request, and detail types; the summary defaults
///     to the detail type.
/// </summary>
/// <typeparam name="TEntity">The persistence entity.</typeparam>
/// <typeparam name="TRequest">The DTO for create and update requests.</typeparam>
/// <typeparam name="TDetail">The DTO for single-resource read responses.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity, TRequest, TDetail> : ResourceAttribute<TEntity, TRequest, TDetail, TDetail>;
