using System;
using Schemata.Common;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>One arm of <see cref="ActivityBehavior.Await" /> waiting on an event definition.</summary>
public sealed class EventBranch
{
    private readonly IEventDefinition _eventDefinition;
    private          Branch[]?        _decisionBranches;
    private          FlowElement?     _target;

    /// <summary>Creates an event branch waiting on <paramref name="eventDefinition" />.</summary>
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

    /// <summary>Adds the catch event and outgoing branch flows to <paramref name="definition" />.</summary>
    internal void Build(ProcessDefinition definition, EventBasedGateway gateway) {
        var catchEvent = new FlowEvent {
            Id         = $"catch_{Identifiers.NewUid():n}",
            Name       = _eventDefinition.Name,
            Position   = EventPosition.IntermediateCatch,
            Definition = _eventDefinition,
        };

        definition.Elements.Add(catchEvent);
        definition.Flows.Add(new() {
                                 Id = $"sf_{Identifiers.NewUid():n}", Source = gateway, Target = catchEvent,
                             });

        if (_decisionBranches is not null) {
            var exclusiveGw = new ExclusiveGateway {
                Id   = $"gateway_{Identifiers.NewUid():n}",
                Name = $"Decision_{_eventDefinition.Name}",
            };
            definition.Elements.Add(exclusiveGw);

            definition.Flows.Add(new() {
                Id = $"sf_{Identifiers.NewUid():n}", Source = catchEvent, Target = exclusiveGw,
            });

            foreach (var branch in _decisionBranches) {
                definition.Flows.Add(new() {
                    Id        = $"sf_{Identifiers.NewUid():n}",
                    Source    = exclusiveGw,
                    Target    = branch.Exit,
                    Condition = branch.Condition,
                    IsDefault = branch.IsDefault,
                });
            }
        } else if (_target is not null) {
            definition.Flows.Add(new() {
                Id = $"sf_{Identifiers.NewUid():n}", Source = catchEvent, Target = _target,
            });
        }
    }
}
