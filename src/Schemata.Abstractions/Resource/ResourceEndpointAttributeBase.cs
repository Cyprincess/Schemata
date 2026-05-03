using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Base class for attributes that declare a named endpoint protocol.
///     Concrete implementations are <see cref="HttpResourceAttribute" /> and
///     <see cref="GrpcResourceAttribute" />.
/// </summary>
public abstract class ResourceEndpointAttributeBase : Attribute
{
    /// <summary>
    ///     Registers this resource as supporting the given endpoint protocol.
    /// </summary>
    /// <param name="endpoint">The endpoint protocol name (e.g., "HTTP", "gRPC").</param>
    public ResourceEndpointAttributeBase(string endpoint) { Endpoint = endpoint; }

    /// <summary>
    ///     The endpoint protocol name used to match and generate routes.
    /// </summary>
    public string Endpoint { get; }
}
