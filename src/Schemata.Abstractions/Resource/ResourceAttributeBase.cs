using System;

namespace Schemata.Abstractions.Resource;

public class ResourceAttributeBase : Attribute
{
    public ResourceAttributeBase(string name) {
        Name = name;
    }

    public string Name { get; }
}
