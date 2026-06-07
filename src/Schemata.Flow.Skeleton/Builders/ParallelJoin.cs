using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>Fluent continuation after <see cref="ParallelFork.Join" />.</summary>
public sealed class ParallelJoin
{
    private readonly ProcessDefinition _definition;
    private readonly ParallelGateway   _gateway;

    internal ParallelJoin(ProcessDefinition definition, ParallelGateway gateway) {
        _definition = definition;
        _gateway    = gateway;
    }

    /// <summary>Continues the flow at <paramref name="target" /> after the parallel join.</summary>
    public ActivityBehavior Go(Activity target) {
        _definition.Flows.Add(new() {
                                  Id = $"sf_{ProcessDefinition.GenerateId()}", Source = _gateway, Target = target,
                              });
        return new(_definition, target);
    }
}
