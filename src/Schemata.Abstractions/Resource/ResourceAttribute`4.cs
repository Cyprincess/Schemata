using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute<TEntity, TRequest, TDetail, TSummary> : ResourceAttribute
{
    public ResourceAttribute() : base(typeof(TEntity), typeof(TRequest), typeof(TDetail), typeof(TSummary)) { }
}
