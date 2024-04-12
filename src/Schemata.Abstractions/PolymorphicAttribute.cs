using System;

namespace Schemata.Abstractions;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class PolymorphicAttribute : Attribute
{
    public PolymorphicAttribute(Type baseType) {
        BaseType = baseType;
    }

    public Type BaseType { get; }

    public string? Name { get; set; }
}
