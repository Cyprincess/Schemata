using System.Linq;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>Fluent continuation of <see cref="ActivityBehavior.Fork" /> handing off to a parallel join.</summary>
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

    /// <summary>Inserts a parallel join gateway joining <paramref name="exits" /> (or the fork's branch exits when empty).</summary>
    public ParallelJoin Join(params Activity[] exits) {
        var joinGateway = new ParallelGateway { Name = $"Join_{_gateway.Name}" };
        _definition.Elements.Add(joinGateway);

        var targetExits = exits.Length > 0 ? exits : _branches.Select(b => b.Exit).ToArray();

        foreach (var exit in targetExits) {
            _definition.Flows.Add(new() { Source = exit, Target = joinGateway });
        }

        return new(_definition, joinGateway);
    }
}
