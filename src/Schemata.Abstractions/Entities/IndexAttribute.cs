using System;

namespace Schemata.Abstractions.Entities;

/// <summary>
///     Declares an index over one or more entity properties.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class IndexAttribute(params string[] properties) : Attribute
{
    /// <summary>
    ///     Gets the indexed properties in declaration order.
    /// </summary>
    public string[] Properties { get; } = properties;

    /// <summary>
    ///     Gets or sets whether the index enforces uniqueness.
    /// </summary>
    public bool IsUnique { get; set; }
}
