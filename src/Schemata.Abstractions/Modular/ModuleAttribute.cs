using System;

namespace Schemata.Abstractions.Modular;

/// <summary>
///     Assembly-level attribute that declares a module type for discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModuleAttribute : Attribute
{
    /// <summary>
    ///     Declares a module for assembly-level discovery.
    /// </summary>
    /// <param name="name">The fully-qualified type name of the module.</param>
    public ModuleAttribute(string name) { Name = name; }

    /// <summary>
    ///     The fully-qualified type name, resolvable via <see cref="Type.GetType(string)" />.
    /// </summary>
    public string Name { get; }
}
