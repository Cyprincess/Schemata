using Schemata.Common;
using Schemata.Flow.Skeleton.Models;

namespace Schemata.Flow.Skeleton.Builders;

/// <summary>Fluent continuation of <see cref="ProcessBuilder.Start(ProcessDefinition)" /> placing the initial event.</summary>
public sealed class StartFlow
{
    private readonly ProcessDefinition _definition;
    private readonly IEventDefinition? _eventDefinition;

    internal StartFlow(ProcessDefinition definition, IEventDefinition? eventDefinition = null) {
        _definition      = definition;
        _eventDefinition = eventDefinition;
    }

    /// <summary>Wires the start event to <paramref name="activity" /> and continues from there.</summary>
    public ActivityBehavior Go(Activity activity) {
        var startEvent = new FlowEvent {
            Id         = $"start_{Identifiers.NewUid():n}",
            Name       = _eventDefinition is not null ? $"Start_{_eventDefinition.Name}" : "Start",
            Position   = EventPosition.Start,
            Definition = _eventDefinition,
        };

        _definition.Elements.Add(startEvent);
        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = startEvent, Target = activity,
        });

        return new(_definition, activity);
    }

    /// <summary>Wires the start event into an event-based gateway waiting on the supplied <paramref name="branches" />.</summary>
    public ProcessDefinition Await(params EventBranch[] branches) {
        var startEvent = new FlowEvent {
            Id         = $"start_{Identifiers.NewUid():n}",
            Name       = _eventDefinition is not null ? $"Start_{_eventDefinition.Name}" : "Start",
            Position   = EventPosition.Start,
            Definition = _eventDefinition,
        };

        _definition.Elements.Add(startEvent);

        var gateway = new EventBasedGateway {
            Id = $"gateway_{Identifiers.NewUid():n}", Name = $"Await_{startEvent.Name}",
        };
        _definition.Elements.Add(gateway);

        _definition.Flows.Add(new() {
            Id = $"sf_{Identifiers.NewUid():n}", Source = startEvent, Target = gateway,
        });

        foreach (var branch in branches) {
            branch.Build(_definition, gateway);
        }

        return _definition;
    }
}
