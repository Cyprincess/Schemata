using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>One arm of <see cref="ActivityBehavior.Decide" /> guarded by an optional condition.</summary>
public sealed class Branch
{
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

    /// <summary>
    ///     Names and registers the anonymous <see cref="NoneTask" /> exit created by
    ///     <c>ProcessBuilder.When</c> / <c>Otherwise</c>. Runs when a gateway wires the branch:
    ///     the gateway name plus the branch position make the name deterministic across rebuilds.
    /// </summary>
    internal void EnsureExitRegistered(ProcessDefinition definition, FlowElement gateway, int index) {
        if (Exit is not NoneTask task || !string.IsNullOrEmpty(task.Name)) {
            return;
        }

        task.Name = IsDefault ? $"Branch_{gateway.Name}_Default" : $"Branch_{gateway.Name}_{index}";
        definition.Elements.Add(task);
    }
}
