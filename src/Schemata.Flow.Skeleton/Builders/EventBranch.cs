using System;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>One arm of <see cref="ActivityBehavior.Await" /> waiting on an event definition.</summary>
public sealed class EventBranch
{
    private readonly IEventDefinition _eventDefinition;
    private          Branch[]?        _decisionBranches;
    private          FlowElement?     _target;

    internal EventBranch(IEventDefinition eventDefinition) { _eventDefinition = eventDefinition; }

    /// <summary>Routes the branch to <paramref name="target" /> when the event fires.</summary>
    public EventBranch Go(FlowElement target) {
        if (_decisionBranches is not null) {
            throw new InvalidOperationException($"Cannot set target on event branch '{
                _eventDefinition.Name
            }' after Decide has been called.");
        }

        _target = target;
        return this;
    }

    /// <summary>Routes the branch to <paramref name="target" />.</summary>
    public EventBranch Go(Activity target) { return Go((FlowElement)target); }

    /// <summary>Routes the branch to <paramref name="target" />.</summary>
    public EventBranch Go(EndEvent target) { return Go((FlowElement)target); }

    /// <summary>Inserts an exclusive gateway after the catch event with the supplied <paramref name="branches" />.</summary>
    public EventBranch Decide(params Branch[] branches) {
        if (_target is not null) {
            throw new InvalidOperationException($"Cannot call Decide on event branch '{
                _eventDefinition.Name
            }' after Go has been called.");
        }

        _decisionBranches = branches;
        return this;
    }

    /// <summary>
    ///     Adds the catch event and outgoing branch flows to <paramref name="definition" />.
    ///     The catch name is scoped by the owning gateway so two catches on the same event
    ///     definition under different gateways stay distinct.
    /// </summary>
    internal void Build(ProcessDefinition definition, EventBasedGateway gateway) {
        var catchEvent = new FlowEvent {
            Name       = $"Catch_{gateway.Name}_{_eventDefinition.Name}",
            Position   = EventPosition.IntermediateCatch,
            Definition = _eventDefinition,
        };

        definition.Elements.Add(catchEvent);
        definition.Flows.Add(new() { Source = gateway, Target = catchEvent });

        if (_decisionBranches is not null) {
            var exclusiveGw = new ExclusiveGateway { Name = $"Decision_{catchEvent.Name}" };
            definition.Elements.Add(exclusiveGw);

            definition.Flows.Add(new() { Source = catchEvent, Target = exclusiveGw });

            for (var i = 0; i < _decisionBranches.Length; i++) {
                var branch = _decisionBranches[i];
                branch.EnsureExitRegistered(definition, exclusiveGw, i);
                definition.Flows.Add(new() {
                    Source    = exclusiveGw,
                    Target    = definition.ResolveEntry(branch.Exit),
                    Condition = branch.Condition,
                    IsDefault = branch.IsDefault,
                });
            }
        } else if (_target is not null) {
            definition.Flows.Add(new() { Source = catchEvent, Target = definition.ResolveEntry(_target) });
        }
    }
}
