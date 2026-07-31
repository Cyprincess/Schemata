using System.Collections.Generic;
using Schemata.Abstractions.Entities;
using Schemata.Common;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>One arm of <see cref="ActivityBehavior.Decide" /> guarded by an optional condition.</summary>
public sealed class Branch : IDescriptive
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

    /// <summary>Label carried onto the gateway edge this branch produces.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Description carried onto the gateway edge this branch produces.</summary>
    public string? Description { get; set; }

    /// <summary>Localized display names carried onto the gateway edge this branch produces.</summary>
    public Dictionary<string, string?>? DisplayNames { get; set; }

    /// <summary>Localized descriptions carried onto the gateway edge this branch produces.</summary>
    public Dictionary<string, string?>? Descriptions { get; set; }

    /// <summary>Chains the branch to continue at <paramref name="target" />.</summary>
    public Branch Go(Activity target) {
        Exit = target;
        return this;
    }

    /// <summary>
    ///     Labels the gateway edge this branch produces. The edge has no declaration site to carry
    ///     <c>[DisplayName]</c>, so this is its only label channel.
    /// </summary>
    /// <param name="displayName">Human-readable edge label.</param>
    /// <param name="description">Optional description of when the branch is taken.</param>
    public Branch Labelled(string displayName, string? description = null) {
        this.Label(displayName, description);
        return this;
    }

    /// <summary>Labels the edge for one language tag.</summary>
    /// <param name="locale">IETF BCP 47 language tag, e.g. <c>"zh-Hans"</c>.</param>
    /// <param name="displayName">Edge label for <paramref name="locale" />.</param>
    /// <param name="description">Description for <paramref name="locale" />.</param>
    public Branch Localized(string locale, string displayName, string? description = null) {
        this.Localize(locale, displayName, description);
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
