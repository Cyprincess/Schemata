using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Declares the
///     <seealso href="https://google.aip.dev/122">AIP-122: Resource names</seealso>
///     canonical resource name pattern for an entity type
///     (e.g., <c>"publishers/{publisher}/books/{book}"</c>).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CanonicalNameAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the
    ///     <see cref="CanonicalNameAttribute" /> class.
    /// </summary>
    /// <param name="name">
    ///     The resource name pattern
    ///     (e.g., <c>"publishers/{publisher}/books/{book}"</c>).
    /// </param>
    public CanonicalNameAttribute(string name) { ResourceName = name; }

    /// <summary>
    ///     The AIP-122 resource name pattern assigned to the attributed type.
    /// </summary>
    public string ResourceName { get; }
}
