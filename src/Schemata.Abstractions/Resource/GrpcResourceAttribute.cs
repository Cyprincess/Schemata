using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares that a resource should be exposed via a gRPC endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GrpcResourceAttribute() : ResourceEndpointAttributeBase(Name)
{
    /// <summary>
    ///     The endpoint type name for gRPC resources.
    /// </summary>
    public const string Name = "gRPC";
}
