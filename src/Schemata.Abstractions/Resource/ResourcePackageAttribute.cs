using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Specifies the API package prefix for a resource, used as route prefix for HTTP and service name for gRPC.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ResourcePackageAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ResourcePackageAttribute" /> class.
    /// </summary>
    /// <param name="package">The package prefix (e.g., "api/v1").</param>
    public ResourcePackageAttribute(string package) { Package = package; }

    /// <summary>
    ///     Gets the package prefix.
    /// </summary>
    public string Package { get; }
}
