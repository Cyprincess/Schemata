using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

public sealed class StartFlow
{
    private readonly ProcessDefinition _definition;
    private readonly IEventDefinition? _eventDefinition;

    internal StartFlow(ProcessDefinition definition, IEventDefinition? eventDefinition = null) {
        _definition      = definition;
        _eventDefinition = eventDefinition;
    }

    public ActivityBehavior Go(Activity activity) {
        var startEvent = new FlowEvent {
            Id         = $"start_{ProcessDefinition.GenerateId()}",
            Name       = _eventDefinition is not null ? $"Start_{_eventDefinition.Name}" : "Start",
            Position   = EventPosition.Start,
            Definition = _eventDefinition,
        };

        _definition.Elements.Add(startEvent);
        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = startEvent, Target = activity,
            }
        );

        return new(_definition, activity);
    }

    public ProcessDefinition Await(params EventBranch[] branches) {
        var startEvent = new FlowEvent {
            Id         = $"start_{ProcessDefinition.GenerateId()}",
            Name       = _eventDefinition is not null ? $"Start_{_eventDefinition.Name}" : "Start",
            Position   = EventPosition.Start,
            Definition = _eventDefinition,
        };

        _definition.Elements.Add(startEvent);

        var gateway = new EventBasedGateway {
            Id = $"gateway_{ProcessDefinition.GenerateId()}", Name = $"Await_{startEvent.Name}",
        };
        _definition.Elements.Add(gateway);

        _definition.Flows.Add(
            new() {
                Id = $"sf_{ProcessDefinition.GenerateId()}", Source = startEvent, Target = gateway,
            }
        );

        foreach (var branch in branches) {
            branch.Build(_definition, gateway);
        }

        return _definition;
    }
}
