using System;

namespace Schemata.Abstractions.Entities;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CanonicalNameAttribute : Attribute
{
    public CanonicalNameAttribute(string name) {
        ResourceName = name;
    }

    public string ResourceName { get; }
}
