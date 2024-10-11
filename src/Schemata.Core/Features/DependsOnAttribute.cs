using System;

namespace Schemata.Core.Features;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DependsOnAttribute : Attribute
{
    public DependsOnAttribute(string name) {
        Name = name;
    }

    public string Name { get; }

    public bool Optional { get; init; }
}
