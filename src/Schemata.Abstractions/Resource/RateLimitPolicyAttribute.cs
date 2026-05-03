using System;

namespace Schemata.Abstractions.Resource;

/// <summary>
///     Associates a named rate-limiting policy with a resource, allowing
///     per-resource throttling without duplicating policy configuration.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class RateLimitPolicyAttribute : Attribute
{
    /// <summary>
    ///     Associates the named policy with the annotated resource.
    /// </summary>
    /// <param name="policyName">The rate-limit policy name to apply.</param>
    public RateLimitPolicyAttribute(string policyName) { PolicyName = policyName; }

    /// <summary>
    ///     The policy name to look up in the rate-limiting configuration.
    /// </summary>
    public string PolicyName { get; }
}
