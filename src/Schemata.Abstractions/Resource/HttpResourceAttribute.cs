using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Marks a resource for exposure via HTTP REST endpoints.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HttpResourceAttribute() : ResourceEndpointAttributeBase(Name)
{
    /// <summary>
    ///     Endpoint protocol name used for HTTP resource exposure.
    /// </summary>
    public const string Name = "HTTP";
}
