using System.Linq;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

public sealed class ParallelFork
{
    private readonly FlowBranch[]      _branches;
    private readonly ProcessDefinition _definition;
    private readonly ParallelGateway   _gateway;

    internal ParallelFork(ProcessDefinition definition, ParallelGateway gateway, FlowBranch[] branches) {
        _definition = definition;
        _gateway    = gateway;
        _branches   = branches;
    }

    public ParallelJoin Join(params Activity[] exits) {
        var joinGateway = new ParallelGateway {
            Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = $"Join_{_gateway.Name}",
        };
        _definition.Elements.Add(joinGateway);

        var targetExits = exits.Length > 0 ? exits : _branches.Select(b => b.Exit).ToArray();

        foreach (var exit in targetExits) {
            _definition.Flows.Add(
                new() {
                    Id = $"sf_{ProcessDefinition.GenerateId()}", Source = exit, Target = joinGateway,
                }
            );
        }

        return new(_definition, joinGateway);
    }
}
