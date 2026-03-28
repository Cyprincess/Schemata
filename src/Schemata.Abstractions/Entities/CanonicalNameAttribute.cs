using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Specifies the AIP-122 canonical resource name pattern for an entity type (e.g.,
///     "publishers/{publisher}/books/{book}").
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CanonicalNameAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="CanonicalNameAttribute" /> class.
    /// </summary>
    /// <param name="name">The resource name pattern (e.g., "publishers/{publisher}/books/{book}").</param>
    public CanonicalNameAttribute(string name) { ResourceName = name; }

    /// <summary>
    ///     Gets the resource name pattern.
    /// </summary>
    public string ResourceName { get; }
}
