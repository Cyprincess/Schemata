using System;

namespace Schemata.Abstractions.Resource;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RateLimitPolicyAttribute : Attribute
{
    public RateLimitPolicyAttribute(string policyName) { PolicyName = policyName; }

    public string PolicyName { get; }
}
