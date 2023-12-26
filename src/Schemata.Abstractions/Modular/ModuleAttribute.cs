using System;

namespace Schemata.Abstractions.Modular;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public class ModuleAttribute(string? name) : Attribute
{
    public string Name { get; } = name ?? string.Empty;
}
