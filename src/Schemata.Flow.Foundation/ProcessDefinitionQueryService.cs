using System.Collections.Generic;
using System.Linq;
using Schemata.Common;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Foundation;

/// <summary>Projects registered Flow process definitions into transport DTOs.</summary>
public sealed class ProcessDefinitionQueryService(IProcessRegistry registry)
{
    /// <summary>Lists registered Flow process definitions.</summary>
    public List<ProcessDefinitionInfo> ListProcessDefinitions() {
        return registry.GetRegisteredProcesses()
                       .Select(n => {
                           var definition = registry.GetRegistration(n)?.Definition;
                           var info = new ProcessDefinitionInfo {
                               CanonicalName = $"definitions/{n}",
                               Elements      = ProjectElements(definition),
                               Flows         = definition?.AllFlows.Select(ProjectFlow).ToList() ?? [],
                               Messages      = definition?.Messages.Select(ProjectMessage).ToList() ?? [],
                           };

                           definition?.CopyLabels(info);
                           return info;
                       })
                       .ToList();
    }

    private static List<ProcessDefinitionElementInfo> ProjectElements(ProcessDefinition? definition) {
        var sink = new List<ProcessDefinitionElementInfo>();
        if (definition is not null) {
            Collect(definition.Elements, null, sink);
        }

        return sink;
    }

    private static void Collect(
        IEnumerable<FlowElement>           elements,
        string?                            scope,
        List<ProcessDefinitionElementInfo> sink
    ) {
        foreach (var element in elements) {
            sink.Add(ProjectElement(element, scope));
            if (element is SubProcess sub) {
                Collect(sub.Children, sub.Name, sink);
            }
        }
    }

    private static ProcessDefinitionElementInfo ProjectElement(FlowElement element, string? scope) {
        var flowEvent = element as FlowEvent;

        var info = new ProcessDefinitionElementInfo {
            Name             = element.Name,
            Kind             = element.GetType().Name,
            Position         = flowEvent?.Position,
            Trigger          = flowEvent?.Definition?.Name,
            TriggerKind      = flowEvent?.Definition?.GetType().Name,
            Scope            = scope,
            AttachedTo       = flowEvent?.AttachedTo?.Name,
            Interrupting     = flowEvent is { Position: EventPosition.Boundary } ? flowEvent.Interrupting : null,
            IsTerminate      = flowEvent is { Position: EventPosition.End } ? flowEvent.IsTerminate : null,
            TriggeredByEvent = element is SubProcess sub ? sub.TriggeredByEvent : null,
            Loop             = ProjectLoop((element as Activity)?.LoopCharacteristics),
        };

        element.CopyLabels(info);
        return info;
    }

    private static LoopKind? ProjectLoop(LoopCharacteristics? characteristics) {
        return characteristics switch {
            StandardLoopCharacteristics                             => LoopKind.Standard,
            MultiInstanceLoopCharacteristics { IsSequential: true } => LoopKind.SequentialMultiInstance,
            MultiInstanceLoopCharacteristics                        => LoopKind.ParallelMultiInstance,
            _                                                       => null,
        };
    }

    private static ProcessDefinitionFlowInfo ProjectFlow(SequenceFlow flow) {
        var info = new ProcessDefinitionFlowInfo {
            Source        = flow.Source.Name,
            Target        = flow.Target.Name,
            IsDefault     = flow.IsDefault,
            IsConditional = flow.Condition is not null,
        };

        flow.CopyLabels(info);
        return info;
    }

    private static ProcessDefinitionMessageInfo ProjectMessage(Message message) {
        var info = new ProcessDefinitionMessageInfo { Name = message.Name };

        message.CopyLabels(info);
        return info;
    }
}
