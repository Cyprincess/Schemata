using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ResourcePackageAttribute : Attribute
{
    public ResourcePackageAttribute(string package) { Package = package; }

    public string Package { get; }
}
