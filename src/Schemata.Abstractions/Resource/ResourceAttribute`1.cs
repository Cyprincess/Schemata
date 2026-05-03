using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Convenience overload per
///     <seealso href="https://google.aip.dev/121">AIP-121: Resource-oriented design</seealso>
///     and <seealso href="https://google.aip.dev/123">AIP-123: Resource types</seealso>
///     where all four type roles share a single type.
///     <c>[Resource&lt;MyEntity&gt;]</c> is equivalent to <c>[Resource(typeof(MyEntity))]</c>.
/// </summary>
/// <typeparam name="TEntity">The type used for entity, request, detail, and summary.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity> : ResourceAttribute<TEntity, TEntity>;
