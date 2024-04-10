using System;
using System.Collections.Generic;
using System.Reflection;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class ResourceAttribute : Attribute
{
    public ResourceAttribute(
        Type  entity,
        Type? request = null,
        Type? detail  = null,
        Type? summary = null) {
        EntityType  = entity;
        RequestType = request ?? entity;
        DetailType  = detail ?? entity;
        SummaryType = summary ?? detail ?? entity;

        Endpoints.AddRange(EntityType.GetCustomAttributes<ResourceAttributeBase>());
    }

    public Type EntityType { get; }

    public Type? RequestType { get; }

    public Type? DetailType { get; }

    public Type? SummaryType { get; }

    public List<ResourceAttributeBase> Endpoints { get; } = [];
}
