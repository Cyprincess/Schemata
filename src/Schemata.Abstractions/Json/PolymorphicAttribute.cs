using System;

namespace Schemata.Abstractions.Json;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class PolymorphicAttribute : Attribute
{
    public PolymorphicAttribute(Type type) {
        BaseType = type;
    }

    public Type BaseType { get; }

    public string? Name { get; set; }
}
