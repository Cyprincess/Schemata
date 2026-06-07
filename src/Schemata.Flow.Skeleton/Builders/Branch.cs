using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>One arm of <see cref="ActivityBehavior.Decide" /> guarded by an optional condition.</summary>
public sealed class Branch
{
    /// <summary>Creates a branch entering at <paramref name="entry" /> with an optional <paramref name="condition" />.</summary>
    public Branch(Activity entry, IConditionExpression? condition = null, bool isDefault = false) {
        Entry     = entry;
        Exit      = entry;
        Condition = condition;
        IsDefault = isDefault;
    }

    /// <summary>Activity the branch enters at when the condition matches.</summary>
    public Activity Entry { get; }

    /// <summary>Activity the branch terminates at after optional <see cref="Go" /> chaining.</summary>
    public Activity Exit { get; private set; }

    /// <summary>Optional guard evaluated against the process variables.</summary>
    public IConditionExpression? Condition { get; }

    /// <summary>When <c>true</c>, the branch is taken if no other branch's condition matches.</summary>
    public bool IsDefault { get; }

    /// <summary>Chains the branch to continue at <paramref name="target" />.</summary>
    public Branch Go(Activity target) {
        Exit = target;
        return this;
    }
}
