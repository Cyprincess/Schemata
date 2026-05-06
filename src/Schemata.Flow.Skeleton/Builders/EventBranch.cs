using System;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

public sealed class EventBranch
{
    private readonly IEventDefinition _eventDefinition;
    private          Branch[]?        _decisionBranches;
    private          FlowElement?     _target;

    internal EventBranch(IEventDefinition eventDefinition) { _eventDefinition = eventDefinition; }

    public EventBranch Go(FlowElement target) {
        if (_decisionBranches is not null) {
            throw new InvalidOperationException(
                $"Cannot set target on event branch '{_eventDefinition.Name}' after Decide has been called."
            );
        }

        _target = target;
        return this;
    }

    public EventBranch Go(Activity target) => Go((FlowElement)target);

    public EventBranch Go(EndEvent target) => Go((FlowElement)target);

    public EventBranch Decide(params Branch[] branches) {
        if (_target is not null) {
            throw new InvalidOperationException(
                $"Cannot call Decide on event branch '{_eventDefinition.Name}' after Go has been called."
            );
        }

        _decisionBranches = branches;
        return this;
    }

    internal void Build(ProcessDefinition definition, EventBasedGateway gateway) {
        var catchEvent = new FlowEvent {
            Id         = $"catch_{ProcessDefinition.GenerateId()}",
            Name       = _eventDefinition.Name,
            Position   = EventPosition.IntermediateCatch,
            Definition = _eventDefinition,
        };

        definition.Elements.Add(catchEvent);
        definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = gateway, Target = catchEvent,
            }
        );

        if (_decisionBranches is not null) {
            var exclusiveGw = new ExclusiveGateway {
                Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = $"Decision_{_eventDefinition.Name}",
            };
            definition.Elements.Add(exclusiveGw);

            definition.Flows.Add(
                new() {
                    Id = $"sf_{ProcessDefinition.GenerateId()}", Source = catchEvent, Target = exclusiveGw,
                }
            );

            foreach (var branch in _decisionBranches) {
                definition.Flows.Add(
                    new() {
                        Id        = $"sf_{ProcessDefinition.GenerateId()}",
                        Source    = exclusiveGw,
                        Target    = branch.Exit,
                        Condition = branch.Condition,
                        IsDefault = branch.IsDefault,
                    }
                );
            }
        } else if (_target is not null) {
            definition.Flows.Add(
                new() {
                    Id = $"sf_{ProcessDefinition.GenerateId()}", Source = catchEvent, Target = _target,
                }
            );
        }
    }
}
