using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GrpcResourceAttribute() : ResourceEndpointAttributeBase(Name)
{
    public const string Name = "gRPC";
}
