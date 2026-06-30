using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;

namespace Schemata.Flow.Bpmn.Conformance.Tests.Adapters;

public static class BpmnXmlAdapter
{
    private static readonly XNamespace Bpmn = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    private static readonly HashSet<string> UnsupportedElements = new(StringComparer.Ordinal) {
        "choreography",
        "collaboration",
        "conditionalEventDefinition",
        "lane",
        "laneSet",
        "linkEventDefinition",
        "multipleEventDefinition",
        "participant",
    };

    public static ProcessDefinition Parse(string filePath) {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var document = XDocument.Load(filePath, LoadOptions.SetLineInfo);
        ThrowIfUnsupported(document.Descendants().Where(IsBpmn));

        var process = document.Descendants(Bpmn + "process").FirstOrDefault();
        if (process is null) {
            throw new InvalidDataException($"BPMN file '{filePath}' does not contain a process element.");
        }

        var names = document.Descendants()
                            .Where(IsBpmn)
                            .Where(element => element.Attribute("id") is not null)
                            .ToDictionary(element => Id(element), DisplayName, StringComparer.Ordinal);

        var definition = new ProcessDefinition {
            Name        = Id(process),
            DisplayName = OptionalName(process),
        };

        var scope = ParseScope(process, names);
        definition.Elements.AddRange(scope.Elements);
        definition.Flows.AddRange(scope.Flows);
        definition.Messages.AddRange(scope.Elements.OfType<FlowEvent>().Select(e => e.Definition).OfType<Message>());
        definition.Signals.AddRange(scope.Elements.OfType<FlowEvent>().Select(e => e.Definition).OfType<Signal>());
        definition.Errors.AddRange(scope.Elements.OfType<FlowEvent>().Select(e => e.Definition).OfType<ErrorDefinition>());
        definition.Escalations.AddRange(scope.Elements.OfType<FlowEvent>().Select(e => e.Definition).OfType<EscalationDefinition>());

        return definition;
    }

    private static Scope ParseScope(XElement owner, IReadOnlyDictionary<string, string> names) {
        var elements = new List<FlowElement>();
        var byId     = new Dictionary<string, FlowElement>(StringComparer.Ordinal);
        var defaults = owner.Elements()
                            .Where(IsBpmn)
                            .Where(element => element.Attribute("id") is not null && element.Attribute("default") is not null)
                            .ToDictionary(element => Id(element), element => element.Attribute("default")!.Value, StringComparer.Ordinal);

        foreach (var element in owner.Elements().Where(IsBpmn)) {
            if (element.Name == Bpmn + "sequenceFlow") {
                continue;
            }

            var flowElement = CreateElement(element, names);
            if (flowElement is null) {
                continue;
            }

            elements.Add(flowElement);
            byId.Add(flowElement.Name, flowElement);
        }

        foreach (var boundary in elements.OfType<FlowEvent>().Where(e => e.Position == EventPosition.Boundary)) {
            var source = owner.Elements(Bpmn + "boundaryEvent").First(e => Id(e) == boundary.Name);
            var attachedToRef = RequiredAttribute(source, "attachedToRef");
            if (!byId.TryGetValue(attachedToRef, out var attached) || attached is not Activity activity) {
                throw new InvalidDataException($"Boundary event '{boundary.Name}' references unknown activity '{attachedToRef}'.");
            }

            boundary.AttachedTo = activity;
        }

        var flows = owner.Elements(Bpmn + "sequenceFlow")
                         .Select(flow => CreateFlow(flow, byId, defaults))
                         .ToList();

        foreach (var flow in flows) {
            AttachFlow(flow);
        }

        return new(elements, flows);
    }

    private static FlowElement? CreateElement(XElement element, IReadOnlyDictionary<string, string> names) {
        return element.Name.LocalName switch {
            "startEvent"              => Event(element, EventPosition.Start, names),
            "endEvent"                => Event(element, EventPosition.End, names),
            "intermediateCatchEvent"  => Event(element, EventPosition.IntermediateCatch, names),
            "intermediateThrowEvent"  => Event(element, EventPosition.IntermediateThrow, names),
            "boundaryEvent"           => BoundaryEvent(element, names),
            "task"                    => Activity<NoneTask>(element),
            "userTask"                => Activity<UserTask>(element),
            "serviceTask"             => Activity<ServiceTask>(element),
            "scriptTask"              => Activity<ScriptTask>(element),
            "manualTask"              => Activity<ManualTask>(element),
            "sendTask"                => Activity<SendTask>(element),
            "receiveTask"             => Activity<ReceiveTask>(element),
            "businessRuleTask"        => Activity<BusinessRuleTask>(element),
            "callActivity"            => CallActivity(element),
            "exclusiveGateway"        => Element<ExclusiveGateway>(element),
            "parallelGateway"         => Element<ParallelGateway>(element),
            "inclusiveGateway"        => Element<InclusiveGateway>(element),
            "eventBasedGateway"       => Element<EventBasedGateway>(element),
            "subProcess"              => SubProcess(element, names),
            "transaction"             => Transaction(element, names),
            "documentation" or "extensionElements" or "incoming" or "outgoing"
                or "dataObjectReference" or "dataStoreReference"
                or "ioSpecification" or "dataInput" or "dataOutput"
                or "inputSet" or "outputSet" or "inputOutputBinding"
                or "dataInputRefs" or "dataOutputRefs" => null,
            var name when UnsupportedElements.Contains(name) => throw Unsupported(name),
            var name => throw Unsupported(name),
        };
    }

    private static FlowEvent Event(XElement element, EventPosition position, IReadOnlyDictionary<string, string> names) {
        var flowEvent = Element<FlowEvent>(element);
        flowEvent.Position    = position;
        flowEvent.Definition  = EventDefinition(element, names) ?? new NoneDefinition { Name = flowEvent.Name };
        flowEvent.IsTerminate = element.Elements(Bpmn + "terminateEventDefinition").Any();
        return flowEvent;
    }

    private static FlowEvent BoundaryEvent(XElement element, IReadOnlyDictionary<string, string> names) {
        var flowEvent = Event(element, EventPosition.Boundary, names);
        flowEvent.Interrupting = element.Attribute("cancelActivity")?.Value is { } cancelActivity
            ? !string.Equals(cancelActivity, "false", StringComparison.OrdinalIgnoreCase)
            : flowEvent.Definition is not EscalationDefinition;
        return flowEvent;
    }

    private static T Activity<T>(XElement element) where T : Activity, new() {
        var activity = Element<T>(element);
        activity.LoopCharacteristics = LoopCharacteristics(element);
        return activity;
    }

    private static CallActivity CallActivity(XElement element) {
        var activity = Activity<CallActivity>(element);
        activity.CalledElement = element.Attribute("calledElement")?.Value ?? string.Empty;
        return activity;
    }

    private static SubProcess SubProcess(XElement element, IReadOnlyDictionary<string, string> names) {
        SubProcess subProcess = string.Equals(element.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase)
            ? Activity<EventSubProcess>(element)
            : Activity<EmbeddedSubProcess>(element);
        subProcess.TriggeredByEvent = string.Equals(element.Attribute("triggeredByEvent")?.Value, "true", StringComparison.OrdinalIgnoreCase);

        var scope = ParseScope(element, names);
        subProcess.Children.AddRange(scope.Elements);
        subProcess.ChildFlows.AddRange(scope.Flows);
        return subProcess;
    }

    private static TransactionSubProcess Transaction(XElement element, IReadOnlyDictionary<string, string> names) {
        var transaction = Activity<TransactionSubProcess>(element);
        var scope       = ParseScope(element, names);
        transaction.Children.AddRange(scope.Elements);
        transaction.ChildFlows.AddRange(scope.Flows);
        return transaction;
    }

    private static T Element<T>(XElement element) where T : FlowElement, new() {
        return new() { Name = Id(element) };
    }

    private static SequenceFlow CreateFlow(
        XElement                            element,
        IReadOnlyDictionary<string, FlowElement> elements,
        IReadOnlyDictionary<string, string>      defaults
    ) {
        var id = Id(element);
        var sourceRef = RequiredAttribute(element, "sourceRef");
        var targetRef = RequiredAttribute(element, "targetRef");

        if (!elements.TryGetValue(sourceRef, out var source)) {
            throw new InvalidDataException($"Sequence flow '{id}' references unknown source '{sourceRef}'.");
        }

        if (!elements.TryGetValue(targetRef, out var target)) {
            throw new InvalidDataException($"Sequence flow '{id}' references unknown target '{targetRef}'.");
        }

        return new() {
            Source    = source,
            Target    = target,
            Condition = Condition(element),
            IsDefault = defaults.TryGetValue(sourceRef, out var defaultFlow) && string.Equals(defaultFlow, id, StringComparison.Ordinal),
        };
    }

    private static IConditionExpression? Condition(XElement flow) {
        var condition = flow.Element(Bpmn + "conditionExpression")?.Value.Trim();
        if (string.IsNullOrEmpty(condition)) {
            return null;
        }

        return new LambdaConditionExpression { Lambda = _ => new(true) };
    }

    private static void AttachFlow(SequenceFlow flow) {
        if (flow.Source is Activity sourceActivity) {
            sourceActivity.Outgoing.Add(flow);
            if (flow.IsDefault) {
                sourceActivity.DefaultFlow = flow;
            }
        } else if (flow.Source is FlowEvent sourceEvent) {
            sourceEvent.Outgoing.Add(flow);
        } else if (flow.Source is Gateway sourceGateway) {
            sourceGateway.Outgoing.Add(flow);
        }

        if (flow.Target is Activity targetActivity) {
            targetActivity.Incoming.Add(flow);
        } else if (flow.Target is FlowEvent targetEvent) {
            targetEvent.Incoming.Add(flow);
        } else if (flow.Target is Gateway targetGateway) {
            targetGateway.Incoming.Add(flow);
        }
    }

    private static LoopCharacteristics? LoopCharacteristics(XElement activity) {
        var standard = activity.Element(Bpmn + "standardLoopCharacteristics");
        if (standard is not null) {
            return new StandardLoopCharacteristics {
                LoopCondition = standard.Element(Bpmn + "loopCondition") is null ? null : TrueCondition(),
                LoopMaximum   = int.TryParse(standard.Attribute("loopMaximum")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) ? max : null,
                TestBefore    = string.Equals(standard.Attribute("testBefore")?.Value, "true", StringComparison.OrdinalIgnoreCase),
            };
        }

        var multi = activity.Element(Bpmn + "multiInstanceLoopCharacteristics");
        if (multi is null) {
            return null;
        }

        var cardinality = multi.Element(Bpmn + "loopCardinality")?.Value.Trim();
        return new MultiInstanceLoopCharacteristics {
            IsSequential = string.Equals(multi.Attribute("isSequential")?.Value, "true", StringComparison.OrdinalIgnoreCase),
            LoopCardinality = string.IsNullOrEmpty(cardinality)
                ? null
                : new LambdaConditionExpression { Lambda = context => Cardinality(context, cardinality) },
            CompletionCondition = multi.Element(Bpmn + "completionCondition") is null ? null : TrueCondition(),
            OneCompletedEventBehavior = MIEventBehavior.All,
        };
    }

    private static ValueTask<bool> Cardinality(FlowConditionContext context, string text) {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)) {
            context.Bookkeeping["loopCardinality"] = count;
            return new(true);
        }

        return new(true);
    }

    private static IConditionExpression TrueCondition() {
        return new LambdaConditionExpression { Lambda = _ => new(true) };
    }

    private static IEventDefinition? EventDefinition(XElement element, IReadOnlyDictionary<string, string> names) {
        var definitions = element.Elements().Where(IsBpmn).Where(child => child.Name.LocalName.EndsWith("EventDefinition", StringComparison.Ordinal)).ToList();
        if (definitions.Count == 0) {
            return null;
        }

        if (definitions.Count > 1) {
            throw Unsupported("multipleEventDefinition");
        }

        var definition = definitions[0];
        return definition.Name.LocalName switch {
            "messageEventDefinition"    => new Message { Name = ReferencedName(definition, "messageRef", names) },
            "signalEventDefinition"     => new Signal { Name = ReferencedName(definition, "signalRef", names) },
            "timerEventDefinition"      => Timer(definition),
            "errorEventDefinition"      => Error(definition, names),
            "escalationEventDefinition" => Escalation(definition, names),
            "cancelEventDefinition"     => new CancelDefinition { Name = DisplayName(definition) },
            "compensateEventDefinition" => new CompensationDefinition { Name = DisplayName(definition) },
            "terminateEventDefinition"  => new NoneDefinition { Name = DisplayName(definition) },
            var name when UnsupportedElements.Contains(name) => throw Unsupported(name),
            var name => throw Unsupported(name),
        };
    }

    private static TimerDefinition Timer(XElement element) {
        if (element.Element(Bpmn + "timeDate") is { } date) {
            return new() { Name = DisplayName(element), TimerType = TimerType.Date, TimeExpression = date.Value.Trim() };
        }

        if (element.Element(Bpmn + "timeCycle") is { } cycle) {
            return new() { Name = DisplayName(element), TimerType = TimerType.Cycle, TimeExpression = cycle.Value.Trim() };
        }

        return new() { Name = DisplayName(element), TimerType = TimerType.Duration, TimeExpression = element.Element(Bpmn + "timeDuration")?.Value.Trim() ?? string.Empty };
    }

    private static ErrorDefinition Error(XElement element, IReadOnlyDictionary<string, string> names) {
        var errorRef = element.Attribute("errorRef")?.Value;
        return new() {
            Name          = errorRef is not null && names.TryGetValue(errorRef, out var name) ? name : DisplayName(element),
            ErrorCode     = element.Attribute("errorCode")?.Value,
            ExceptionType = typeof(Exception),
        };
    }

    private static EscalationDefinition Escalation(XElement element, IReadOnlyDictionary<string, string> names) {
        var escalationRef = element.Attribute("escalationRef")?.Value;
        return new() {
            Name           = escalationRef is not null && names.TryGetValue(escalationRef, out var name) ? name : DisplayName(element),
            EscalationCode = element.Attribute("escalationCode")?.Value,
        };
    }

    private static string ReferencedName(XElement element, string attribute, IReadOnlyDictionary<string, string> names) {
        var reference = element.Attribute(attribute)?.Value;
        if (reference is not null && names.TryGetValue(reference, out var name)) {
            return name;
        }

        return DisplayName(element);
    }

    private static void ThrowIfUnsupported(IEnumerable<XElement> elements) {
        foreach (var element in elements) {
            if (UnsupportedElements.Contains(element.Name.LocalName)) {
                throw Unsupported(element.Name.LocalName);
            }
        }
    }

    private static NotSupportedException Unsupported(string elementName) {
        return new($"BPMN element '{elementName}' is not supported by Schemata engine — mark vector as Pending");
    }

    private static bool IsBpmn(XElement element) { return element.Name.Namespace == Bpmn; }

    private static string Id(XElement element) { return RequiredAttribute(element, "id"); }

    private static string DisplayName(XElement element) { return OptionalName(element) ?? element.Attribute("id")?.Value ?? element.Name.LocalName; }

    private static string? OptionalName(XElement element) { return element.Attribute("name")?.Value; }

    private static string RequiredAttribute(XElement element, string name) {
        var value = element.Attribute(name)?.Value;
        if (value is null) {
            throw new InvalidDataException(
                $"BPMN element '{element.Name.LocalName}' is missing required '{name}' attribute."
            );
        }

        return value;
    }

    private sealed record Scope(List<FlowElement> Elements, List<SequenceFlow> Flows);
}
