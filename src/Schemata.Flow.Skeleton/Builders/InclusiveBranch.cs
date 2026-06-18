using Schemata.Common;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>Fluent continuation of <see cref="ActivityBehavior.Include" /> handing off to an inclusive merge.</summary>
public sealed class InclusiveBranch
{
    private readonly ProcessDefinition _definition;
    private readonly InclusiveGateway  _gateway;

    internal InclusiveBranch(ProcessDefinition definition, InclusiveGateway gateway) {
        _definition = definition;
        _gateway    = gateway;
    }

    /// <summary>Inserts an inclusive merge gateway joining <paramref name="exits" />.</summary>
    public InclusiveMerge Merge(params Activity[] exits) {
        var mergeGateway = new InclusiveGateway {
            Id = $"gateway_{Identifiers.NewUid():n}", Name = $"Merge_{_gateway.Name}",
        };
        _definition.Elements.Add(mergeGateway);

        foreach (var exit in exits) {
            _definition.Flows.Add(new() {
                Id = $"sf_{Identifiers.NewUid():n}", Source = exit, Target = mergeGateway,
            });
        }

        return new(_definition, mergeGateway);
    }
}
