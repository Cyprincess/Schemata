using System;

namespace Schemata.Abstractions.Json;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PolymorphicAttribute(Type type) : Attribute
{
    public Type BaseType { get; } = type;

    public string? Name { get; set; }
}
