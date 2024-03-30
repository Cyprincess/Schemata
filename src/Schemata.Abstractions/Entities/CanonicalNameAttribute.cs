using System;

namespace Schemata.Abstractions.Entities;

[AttributeUsage(AttributeTargets.Class)]
public class CanonicalNameAttribute(string name) : Attribute
{
    public string ResourceName { get; } = name;
}
