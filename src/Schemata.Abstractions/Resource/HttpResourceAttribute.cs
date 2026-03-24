using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Declares that a resource should be exposed via an HTTP REST endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HttpResourceAttribute() : ResourceEndpointAttributeBase(Name)
{
    /// <summary>
    ///     The endpoint type name for HTTP resources.
    /// </summary>
    public const string Name = "HTTP";
}
