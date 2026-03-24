using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Associates a named rate-limiting policy with a resource.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RateLimitPolicyAttribute : Attribute
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="RateLimitPolicyAttribute" /> class.
    /// </summary>
    /// <param name="policyName">The name of the rate-limiting policy to apply.</param>
    public RateLimitPolicyAttribute(string policyName) { PolicyName = policyName; }

    /// <summary>
    ///     Gets the name of the rate-limiting policy.
    /// </summary>
    public string PolicyName { get; }
}
