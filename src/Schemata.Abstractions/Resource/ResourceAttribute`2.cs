using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity, TRequest> : ResourceAttribute<TEntity, TRequest, TEntity>;
