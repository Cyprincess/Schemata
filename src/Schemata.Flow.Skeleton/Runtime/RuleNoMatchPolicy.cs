namespace Schemata.Flow.Skeleton.Runtime;

/// <summary>The policy a <see cref="Models.BusinessRuleTask" /> applies when its evaluation matches no rule.</summary>
public enum RuleNoMatchPolicy
{
    /// <summary>Fails the operation when no rule matches; nothing is persisted.</summary>
    Fail,

    /// <summary>Treats an unmatched evaluation as no output and advances.</summary>
    Continue,
}
