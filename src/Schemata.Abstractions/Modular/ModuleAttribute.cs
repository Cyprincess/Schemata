using System;

namespace Schemata.Abstractions.Modular;

/// <summary>
///     Assembly-level attribute that declares a module by name for discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModuleAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="ModuleAttribute" /> class.
    /// </summary>
    /// <param name="name">The fully-qualified type name of the module.</param>
    public ModuleAttribute(string? name) { Name = name ?? string.Empty; }

    /// <summary>
    ///     Gets the fully-qualified type name of the module.
    /// </summary>
    public string Name { get; }
}
