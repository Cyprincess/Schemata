using Schemata.Common;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>Fluent continuation after <see cref="ParallelFork.Join" />.</summary>
public sealed class ParallelJoin
{
    private readonly ProcessDefinition _definition;
    private readonly ParallelGateway   _gateway;

    /// <summary>Creates a parallel join continuation for <paramref name="gateway" />.</summary>
    internal ParallelJoin(ProcessDefinition definition, ParallelGateway gateway) {
        _definition = definition;
        _gateway    = gateway;
    }

    /// <summary>Continues the flow at <paramref name="target" /> after the parallel join.</summary>
    public ActivityBehavior Go(Activity target) {
        _definition.Flows.Add(new() {
                                  Id = $"sf_{Identifiers.NewUid():n}", Source = _gateway, Target = target,
                              });
        return new(_definition, target);
    }
}
