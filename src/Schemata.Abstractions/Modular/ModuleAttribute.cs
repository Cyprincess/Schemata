using System;

namespace Schemata.Abstractions.Modular;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class ModuleAttribute : Attribute
{
    public ModuleAttribute(string? name) {
        Name = name ?? string.Empty;
    }

    public string Name { get; }
}
