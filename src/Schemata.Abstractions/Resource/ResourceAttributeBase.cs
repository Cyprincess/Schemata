using System;

namespace Schemata.Abstractions.Resource;

public class ResourceAttributeBase(string name) : Attribute
{
    public string Name { get; } = name;
}
