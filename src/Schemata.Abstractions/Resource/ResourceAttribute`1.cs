using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares a resource API where entity, request, detail, and summary all use the same type.
/// </summary>
/// <typeparam name="TEntity">The entity type used for all roles.</typeparam>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity> : ResourceAttribute<TEntity, TEntity>;
