using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Moq;
using Schemata.Abstractions.Entities;
using Schemata.Flow.Foundation.Handlers;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Tests;

public class ProcessDefinitionGraphProjectionShould
{
    private const string ConditionText = "amount > 1000 /* guard-sentinel */";

    [Fact]
    public async Task Place_Every_Element_In_Its_Own_Scope() {
        var info = await Project();

        Assert.Null(Element(info, "start").Scope);
        Assert.Null(Element(info, "sub").Scope);
        Assert.Equal("sub", Element(info, "subStart").Scope);
        Assert.Equal("sub", Element(info, "work").Scope);
        Assert.Equal("sub", Element(info, "subEnd").Scope);
    }

    [Fact]
    public async Task Attach_Boundary_Events_To_Their_Host() {
        var info = await Project();

        Assert.Equal("sub", Element(info, "onTimeout").AttachedTo);
        Assert.Equal("sub", Element(info, "onNudge").AttachedTo);
        Assert.True(Element(info, "onTimeout").Interrupting);
        Assert.False(Element(info, "onNudge").Interrupting);
        Assert.Null(Element(info, "work").Interrupting);
    }

    [Fact]
    public async Task Report_The_Event_Definition_Shape_Not_Only_Its_Name() {
        var info = await Project();

        Assert.Equal("TimerDefinition", Element(info, "onTimeout").TriggerKind);
        Assert.Equal("Message", Element(info, "onNudge").TriggerKind);
        Assert.Equal("Signal", Element(info, "awaitCancel").TriggerKind);
        Assert.Equal("cancelled", Element(info, "awaitCancel").Trigger);
        Assert.Null(Element(info, "work").TriggerKind);
    }

    [Fact]
    public async Task Report_Termination_And_Loop_And_Event_Sub_Process_Shapes() {
        var info = await Project();

        Assert.True(Element(info, "abort").IsTerminate);
        Assert.False(Element(info, "done").IsTerminate);
        Assert.Null(Element(info, "work").IsTerminate);

        Assert.Equal(LoopKind.ParallelMultiInstance, Element(info, "work").Loop);
        Assert.Null(Element(info, "start").Loop);

        Assert.False(Element(info, "sub").TriggeredByEvent);
        Assert.Null(Element(info, "start").TriggeredByEvent);
    }

    [Fact]
    public async Task Mark_Conditional_And_Default_Edges() {
        var info = await Project();

        var conditional = Flow(info, "decide", "abort");
        Assert.True(conditional.IsConditional);
        Assert.False(conditional.IsDefault);

        var fallback = Flow(info, "decide", "done");
        Assert.False(fallback.IsConditional);
        Assert.True(fallback.IsDefault);
    }

    [Fact]
    public async Task Render_Every_Node_Event_Definition_Message_And_Edge_Without_A_Client_Dictionary() {
        var info = await Project();

        Assert.All(info.Elements, element => Assert.False(string.IsNullOrEmpty(element.DisplayName)));
        Assert.All(info.Flows, flow => Assert.False(string.IsNullOrEmpty(flow.DisplayName)));
        Assert.All(info.Messages, message => Assert.False(string.IsNullOrEmpty(message.DisplayName)));

        Assert.Equal("超时", Element(info, "onTimeout").DisplayNames!["zh-Hans"]);
        Assert.Equal("催办消息", Assert.Single(info.Messages).DisplayNames!["zh-Hans"]);
        Assert.Equal("金额超限", Flow(info, "decide", "abort").DisplayNames!["zh-Hans"]);
    }

    [Fact]
    public async Task Keep_Condition_Expressions_Off_The_Wire() {
        var json = JsonSerializer.Serialize(await Project());

        Assert.DoesNotContain("guard-sentinel", json);
    }

    [Fact]
    public async Task Keep_Element_Names_Stable_When_Labels_Change() {
        var before = (await Project()).Elements.Select(e => e.Name).ToArray();

        var definition = Definition();
        foreach (var element in definition.AllElements) {
            element.DisplayName  = "renamed";
            element.DisplayNames = null;
        }

        Assert.Equal(before, (await Project(definition)).Elements.Select(e => e.Name).ToArray());
    }

    private static ProcessDefinitionElementInfo Element(ProcessDefinitionInfo info, string name) {
        return Assert.Single(info.Elements, element => element.Name == name);
    }

    private static ProcessDefinitionFlowInfo Flow(ProcessDefinitionInfo info, string source, string target) {
        return Assert.Single(info.Flows, flow => flow.Source == source && flow.Target == target);
    }

    private static async Task<ProcessDefinitionInfo> Project(ProcessDefinition? definition = null) {
        var registration = new ProcessRegistration {
            Name          = "orders",
            Engine        = "StateMachine",
            Definition    = definition ?? Definition(),
            Configuration = new() { Name = "orders" },
        };

        var registry = new Mock<IProcessRegistry>();
        registry.Setup(r => r.GetRegisteredProcesses()).Returns(["orders"]);
        registry.Setup(r => r.GetRegistration("orders")).Returns(registration);

        return Assert.Single(await new DefaultListProcessDefinitionsHandler(registry.Object).HandleAsync(new()));
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