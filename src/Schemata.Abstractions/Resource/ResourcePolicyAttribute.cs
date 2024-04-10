using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public class ResourcePolicyAttribute : Attribute
{
    public ResourcePolicyAttribute() { }

    public ResourcePolicyAttribute(string policy) {
        Policy = policy;
    }

    public string? Policy { get; set; }

    public string? Methods { get; set; }

    public string? Roles { get; set; }

    public string? AuthenticationSchemes { get; set; }
}
