using System;

namespace Schemata.Abstractions.Entities;

[AttributeUsage(AttributeTargets.Class)]
public sealed class CanonicalNameAttribute(string name) : Attribute
{
    public string ResourceName { get; } = name;
}
