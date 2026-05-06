using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

public sealed class InclusiveMerge
{
    private readonly ProcessDefinition _definition;
    private readonly InclusiveGateway  _gateway;

    internal InclusiveMerge(ProcessDefinition definition, InclusiveGateway gateway) {
        _definition = definition;
        _gateway    = gateway;
    }

    public ActivityBehavior Go(Activity target) {
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = _gateway, Target = target,
            }
        );
        return new(_definition, target);
    }
}
