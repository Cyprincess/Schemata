using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Foundation;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessDefinitionGraphProjectionShould
{
    private const string ConditionText = "amount > 1000 /* guard-sentinel */";

    [Fact]
    public void PlaceEveryElementInItsOwnScope() {
        var info = Project();

        Assert.Null(Element(info, "start").Scope);
        Assert.Null(Element(info, "sub").Scope);
        Assert.Equal("sub", Element(info, "subStart").Scope);
        Assert.Equal("sub", Element(info, "work").Scope);
        Assert.Equal("sub", Element(info, "subEnd").Scope);
    }

    [Fact]
    public void AttachBoundaryEventsToTheirHost() {
        var info = Project();

        Assert.Equal("sub", Element(info, "onTimeout").AttachedTo);
        Assert.Equal("sub", Element(info, "onNudge").AttachedTo);
        Assert.True(Element(info, "onTimeout").Interrupting);
        Assert.False(Element(info, "onNudge").Interrupting);
        Assert.Null(Element(info, "work").Interrupting);
    }

    [Fact]
    public void ReportTheEventDefinitionShapeNotOnlyItsName() {
        var info = Project();

        Assert.Equal("TimerDefinition", Element(info, "onTimeout").TriggerKind);
        Assert.Equal("Message", Element(info, "onNudge").TriggerKind);
        Assert.Equal("Signal", Element(info, "awaitCancel").TriggerKind);
        Assert.Equal("cancelled", Element(info, "awaitCancel").Trigger);
        Assert.Null(Element(info, "work").TriggerKind);
    }

    [Fact]
    public void ReportTerminationAndLoopAndEventSubProcessShapes() {
        var info = Project();

        Assert.True(Element(info, "abort").IsTerminate);
        Assert.False(Element(info, "done").IsTerminate);
        Assert.Null(Element(info, "work").IsTerminate);

        Assert.Equal(LoopKind.ParallelMultiInstance, Element(info, "work").Loop);
        Assert.Null(Element(info, "start").Loop);

        Assert.False(Element(info, "sub").TriggeredByEvent);
        Assert.Null(Element(info, "start").TriggeredByEvent);
    }

    [Fact]
    public void MarkConditionalAndDefaultEdges() {
        var info = Project();

        var conditional = Flow(info, "decide", "abort");
        Assert.True(conditional.IsConditional);
        Assert.False(conditional.IsDefault);

        var fallback = Flow(info, "decide", "done");
        Assert.False(fallback.IsConditional);
        Assert.True(fallback.IsDefault);
    }

    [Fact]
    public void RenderEveryNodeEventDefinitionMessageAndEdgeWithoutAClientDictionary() {
        var info = Project();

        Assert.All(info.Elements, element => Assert.False(string.IsNullOrEmpty(element.DisplayName)));
        Assert.All(info.Flows, flow => Assert.False(string.IsNullOrEmpty(flow.DisplayName)));
        Assert.All(info.Messages, message => Assert.False(string.IsNullOrEmpty(message.DisplayName)));

        Assert.Equal("超时", Element(info, "onTimeout").DisplayNames!["zh-Hans"]);
        Assert.Equal("催办消息", Assert.Single(info.Messages).DisplayNames!["zh-Hans"]);
        Assert.Equal("金额超限", Flow(info, "decide", "abort").DisplayNames!["zh-Hans"]);
    }

    [Fact]
    public void KeepConditionExpressionsOffTheWire() {
        var json = JsonSerializer.Serialize(Project());

        Assert.DoesNotContain("guard-sentinel", json);
    }

    [Fact]
    public void KeepElementNamesStableWhenLabelsChange() {
        var before = Project().Elements.Select(e => e.Name).ToArray();

        var definition = Definition();
        foreach (var element in definition.AllElements) {
            element.DisplayName  = "renamed";
            element.DisplayNames = null;
        }

        Assert.Equal(before, Project(definition).Elements.Select(e => e.Name).ToArray());
    }

    private static ProcessDefinitionElementInfo Element(ProcessDefinitionInfo info, string name) {
        return Assert.Single(info.Elements, element => element.Name == name);
    }

    private static ProcessDefinitionFlowInfo Flow(ProcessDefinitionInfo info, string source, string target) {
        return Assert.Single(info.Flows, flow => flow.Source == source && flow.Target == target);
    }

    private static ProcessDefinitionInfo Project(ProcessDefinition? definition = null) {
        var registration = new ProcessRegistration {
            Name          = "orders",
            Engine        = "StateMachine",
            Definition    = definition ?? Definition(),
            Configuration = new() { Name = "orders" },
        };

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegisteredProcesses()).Returns(["orders"]);
        registry.Setup(r => r.GetRegistration("orders")).Returns(registration);

        return Assert.Single(new ProcessDefinitionQueryService(registry.Object).ListProcessDefinitions());
    }

    private static ProcessDefinition Definition() {
        var nudge = new Message {
            Name         = "nudge",
            DisplayName  = "Nudge",
            DisplayNames = new() { ["zh-Hans"] = "催办消息" },
        };
        var cancelled = new Signal { Name = "cancelled", DisplayName = "Cancelled" };
        var timeout   = new TimerDefinition { Name = "timeout", TimeExpression = "PT1H", DisplayName = "Timeout" };

        var start    = new StartEvent { Name = "start", Position = EventPosition.Start, DisplayName = "Start" };
        var subStart = new StartEvent { Name = "subStart", Position = EventPosition.Start, DisplayName = "Sub start" };
        var work = new ServiceTask {
            Name                = "work",
            DisplayName         = "Work",
            LoopCharacteristics = new MultiInstanceLoopCharacteristics { IsSequential = false },
        };
        var subEnd = new EndEvent { Name = "subEnd", Position = EventPosition.End, DisplayName = "Sub end" };

        var sub = new EmbeddedSubProcess { Name = "sub", DisplayName = "Sub" };
        sub.Children.AddRange([subStart, work, subEnd]);
        sub.ChildFlows.AddRange([
            new() { Source = subStart, Target = work, DisplayName = "to work" },
            new() { Source = work, Target = subEnd, DisplayName = "to sub end" },
        ]);

        var onTimeout = new FlowEvent {
            Name         = "onTimeout",
            Position     = EventPosition.Boundary,
            Definition   = timeout,
            AttachedTo   = sub,
            Interrupting = true,
            DisplayName  = "On timeout",
            DisplayNames = new() { ["zh-Hans"] = "超时" },
        };
        var onNudge = new FlowEvent {
            Name         = "onNudge",
            Position     = EventPosition.Boundary,
            Definition   = nudge,
            AttachedTo   = sub,
            Interrupting = false,
            DisplayName  = "On nudge",
        };

        var gateway = new EventBasedGateway { Name = "await", DisplayName = "Await" };
        var awaitCancel = new FlowEvent {
            Name        = "awaitCancel",
            Position    = EventPosition.IntermediateCatch,
            Definition  = cancelled,
            DisplayName = "Await cancel",
        };

        var decide = new ExclusiveGateway { Name = "decide", DisplayName = "Decide" };
        var abort = new FlowEvent {
            Name        = "abort",
            Position    = EventPosition.End,
            IsTerminate = true,
            DisplayName = "Abort",
        };
        var done = new EndEvent { Name = "done", Position = EventPosition.End, DisplayName = "Done" };

        var definition = new ProcessDefinition { Name = "orders", DisplayName = "Orders" };
        definition.Elements.AddRange([start, sub, onTimeout, onNudge, gateway, awaitCancel, decide, abort, done]);
        definition.Messages.Add(nudge);
        definition.Signals.Add(cancelled);
        definition.Flows.AddRange([
            new() { Source = start, Target = sub, DisplayName = "to sub" },
            new() { Source = sub, Target = gateway, DisplayName = "to await" },
            new() { Source = onTimeout, Target = decide, DisplayName = "on timeout" },
            new() { Source = onNudge, Target = gateway, DisplayName = "on nudge" },
            new() { Source = gateway, Target = awaitCancel, DisplayName = "to cancel catch" },
            new() { Source = awaitCancel, Target = decide, DisplayName = "to decide" },
            new() {
                Source       = decide,
                Target       = abort,
                Condition    = new SourceStringConditionExpression<Ticket>("ticket", ConditionText),
                DisplayName  = "Over limit",
                DisplayNames = new() { ["zh-Hans"] = "金额超限" },
            },
            new() { Source = decide, Target = done, IsDefault = true, DisplayName = "Otherwise" },
        ]);

        return definition;
    }

    private sealed class Ticket : ICanonicalName
    {
        public string? Name          { get; set; }
        public string? CanonicalName { get; set; }
    }
}

public class ProcessDefinitionLabelIdentityShould
{
    [Fact]
    public void KeepThePropertyNameAsIdentity_WhenALabelIsDeclared() {
        var definition = new LabelledProcess();

        var approval = Assert.Single(definition.Elements);

        Assert.Equal("Approval", approval.Name);
        Assert.Equal("Approval task", approval.DisplayName);
        Assert.Equal("Routes the request to an approver.", approval.Description);
        Assert.Equal("审批", approval.DisplayNames!["zh-Hans"]);
        Assert.Equal("把请求路由给审批人。", approval.Descriptions!["zh-Hans"]);
    }

    [Fact]
    public void LabelEventDefinitionsDeclaredAsMagicProperties() {
        var definition = new LabelledProcess();

        var message = Assert.Single(definition.Messages);

        Assert.Equal("Nudge", message.Name);
        Assert.Equal("催办", message.DisplayNames!["zh-Hans"]);
    }

    [Fact]
    public void LabelTheDefinitionItself_WhenDeclaredOnTheDefinitionClass() {
        var definition = new LabelledDefinition();

        Assert.Equal("Expense approval", definition.DisplayName);
        Assert.Equal("Routes an expense claim to an approver.", definition.Description);
        Assert.Equal("费用审批", definition.DisplayNames!["zh-Hans"]);
        Assert.Equal("把费用申请路由给审批人。", definition.Descriptions!["zh-Hans"]);
    }

    [DisplayName("Expense approval")]
    [Description("Routes an expense claim to an approver.")]
    [Localized("zh-Hans", "费用审批", "把费用申请路由给审批人。")]
    private sealed class LabelledDefinition : ProcessDefinition
    {
        public UserTask Approval { get; private set; } = null!;
    }

    private sealed class LabelledProcess : ProcessDefinition
    {
        [DisplayName("Approval task")]
        [Description("Routes the request to an approver.")]
        [Localized("zh-Hans", "审批", "把请求路由给审批人。")]
        public UserTask Approval { get; private set; } = null!;

        [Localized("zh-Hans", "催办", "催办消息")]
        public Message Nudge { get; private set; } = null!;
    }
}
