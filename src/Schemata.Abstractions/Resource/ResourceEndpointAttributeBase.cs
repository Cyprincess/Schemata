using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Base class for attributes that declare which endpoint types a resource supports.
/// </summary>
public abstract class ResourceEndpointAttributeBase : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ResourceEndpointAttributeBase" /> class.
    /// </summary>
    /// <param name="endpoint">The endpoint type name (e.g., "HTTP", "gRPC").</param>
    public ResourceEndpointAttributeBase(string endpoint) { Endpoint = endpoint; }

    /// <summary>
    ///     Gets the endpoint type name.
    /// </summary>
    public string Endpoint { get; }
}
