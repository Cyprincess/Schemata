using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HttpResourceAttribute() : ResourceEndpointAttributeBase(Name)
{
    public const string Name = "HTTP";
}
