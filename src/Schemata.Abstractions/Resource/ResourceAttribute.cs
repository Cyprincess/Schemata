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
        Entity  = entity;
        Request = request ?? entity;
        Detail  = detail ?? entity;
        Summary = summary ?? detail ?? entity;

        Endpoints.AddRange(Entity.GetCustomAttributes<ResourceAttributeBase>());
    }

    public Type Entity { get; }

    public Type? Request { get; }

    public Type? Detail { get; }

    public Type? Summary { get; }

    public List<ResourceAttributeBase> Endpoints { get; } = [];
}
