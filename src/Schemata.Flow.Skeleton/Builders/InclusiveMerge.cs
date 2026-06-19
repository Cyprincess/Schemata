using Schemata.Common;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>Fluent continuation after <see cref="InclusiveBranch.Merge" />.</summary>
public sealed class InclusiveMerge
{
    private readonly ProcessDefinition _definition;
    private readonly InclusiveGateway  _gateway;

    /// <summary>Creates an inclusive merge continuation for <paramref name="gateway" />.</summary>
    internal InclusiveMerge(ProcessDefinition definition, InclusiveGateway gateway) {
        _definition = definition;
        _gateway    = gateway;
    }

    /// <summary>Continues the flow at <paramref name="target" /> after the merge.</summary>
    public ActivityBehavior Go(Activity target) {
        _definition.Flows.Add(new() {
                                  Id = $"sf_{Identifiers.NewUid():n}", Source = _gateway, Target = target,
                              });
        return new(_definition, target);
    }
}
