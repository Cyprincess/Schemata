using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Sets the API package prefix for a resource. Used as the route prefix in
///     HTTP (e.g. <c>/api/v1/</c>) and the gRPC service namespace.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ResourcePackageAttribute : Attribute
{
    /// <summary>
    ///     Sets the package prefix for the annotated resource.
    /// </summary>
    /// <param name="package">The prefix string (e.g., "api/v1").</param>
    public ResourcePackageAttribute(string package) { Package = package; }

    /// <summary>
    ///     The package prefix applied to generated routes and service names.
    /// </summary>
    public string Package { get; }
}
