using System;

namespace Schemata.Core.Features;

/// <summary>
///     Declares that a feature depends on another feature identified by name.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="DependsOnAttribute" /> class.
    /// </summary>
    /// <param name="name">The fully-qualified type name of the dependency.</param>
    public DependsOnAttribute(string name) { Name = name; }

    /// <summary>
    ///     Gets the fully-qualified type name of the dependency.
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///     Gets or sets whether this dependency is optional.
    /// </summary>
    public bool Optional { get; init; }
}
