using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;
using DerivedEngine = Schemata.Flow.Bpmn.BpmnEngine<
    Schemata.Flow.Bpmn.Tests.DerivedProcess,
    Schemata.Flow.Bpmn.Tests.DerivedToken,
    Schemata.Flow.Bpmn.Tests.DerivedTransition>;

namespace Schemata.Flow.Bpmn.Tests;

[Trait("Layer", "Unit")]
public sealed class BpmnEngine_DerivedTokenShould
{
    [Fact]
    public async Task Trigger_EventSubProcess_StagesDerivedChildWithFactoryStateAndLocalScope() {
        var signal = new Signal { Name = "derived-trigger" };
        var definition = TriggerDefinition(signal);
        var process = NewProcess(definition.Name);
        var staged = new List<SchemataProcessToken>();
        var engine = new DerivedEngine();
        var started = await engine.StartAsync(definition, process, Context(staged), CancellationToken.None);
        var addressed = Assert.IsType<DerivedToken>(Assert.Single(started.Tokens));
        addressed.Bookkeeping["trigger"] = 17;

        await engine.TriggerAsync(definition, process, [addressed], Context(staged), signal, null, addressed.CanonicalName, CancellationToken.None);

        var child = Assert.IsType<DerivedToken>(staged[^1]);
        Assert.Equal(process.CanonicalName, child.Process);
        Assert.Equal(addressed.CanonicalName, child.Spawner);
        Assert.Equal("event-task", child.StateName);
        Assert.Null(child.WaitingAtName);
        Assert.Equal("Active", child.State);
        Assert.Equal("event-sub", child.ScopeName);
        Assert.Equal(17, child.Bookkeeping["trigger"]);
        Assert.NotSame(addressed.Bookkeeping, child.Bookkeeping);
    }

    [Fact]
    public async Task Escalation_EventSubProcess_StagesDerivedChildWithFactoryStateAndLocalScope() {
        var definition = EscalationDefinition();
        var process = NewProcess(definition.Name);
        var staged = new List<SchemataProcessToken>();
        var engine = new DerivedEngine();
        var started = await engine.StartAsync(definition, process, Context(staged), CancellationToken.None);
        var root = Assert.IsType<DerivedToken>(Assert.Single(started.Tokens));
        var entered = await engine.AdvanceAsync(definition, process, [root], Context(staged), root.CanonicalName, CancellationToken.None);
        var throwing = Assert.IsType<DerivedToken>(Assert.Single(entered.Tokens, token => token.ScopeName == "host"));
        throwing.Bookkeeping["escalation"] = 23;

        await engine.AdvanceAsync(definition, process, entered.Tokens.Cast<DerivedToken>().ToList(), Context(staged), throwing.CanonicalName, CancellationToken.None);

        var child = Assert.IsType<DerivedToken>(staged[^1]);
        Assert.Equal(process.CanonicalName, child.Process);
        Assert.Equal(throwing.CanonicalName, child.Spawner);
        Assert.Equal("event-handler", child.StateName);
        Assert.Null(child.WaitingAtName);
        Assert.Equal("Active", child.State);
        Assert.Equal("escalation-sub", child.ScopeName);
        Assert.Equal(23, child.Bookkeeping["escalation"]);
        Assert.NotSame(throwing.Bookkeeping, child.Bookkeeping);
    }

    private static ProcessDefinition TriggerDefinition(Signal signal) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task = new NoneTask { Name = "task" };
        var end = new FlowEvent { Name = "end", Position = EventPosition.End };
        var eventSub = new EventSubProcess { Name = "event-sub" };
        var eventStart = new FlowEvent {
            Name = "event-start", Position = EventPosition.Start, Definition = signal, Interrupting = false,
        };
        var eventTask = new NoneTask { Name = "event-task" };
        var eventEnd = new FlowEvent { Name = "event-end", Position = EventPosition.End };
        eventSub.Children.Add(eventStart);
        eventSub.Children.Add(eventTask);
        eventSub.Children.Add(eventEnd);
        eventSub.ChildFlows.Add(new() { Source = eventStart, Target = eventTask });
        eventSub.ChildFlows.Add(new() { Source = eventTask, Target = eventEnd });
        return new() {
            Name = "derived-trigger",
            Elements = { start, task, end, eventSub },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = end },
            },
        };
    }

    private static ProcessDefinition EscalationDefinition() {
        var escalation = new EscalationDefinition { Name = "derived-escalation", EscalationCode = "derived" };
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var rootTask = new NoneTask { Name = "root-task" };
        var host = new EmbeddedSubProcess { Name = "host" };
        var rootEnd = new FlowEvent { Name = "root-end", Position = EventPosition.End };
        var hostStart = new FlowEvent { Name = "host-start", Position = EventPosition.Start };
        var setup = new NoneTask { Name = "setup" };
        var throwEvent = new FlowEvent {
            Name = "throw", Position = EventPosition.IntermediateThrow, Definition = escalation,
        };
        var afterThrow = new NoneTask { Name = "after-throw" };
        var hostEnd = new FlowEvent { Name = "host-end", Position = EventPosition.End };
        host.Children.Add(hostStart);
        host.Children.Add(setup);
        host.Children.Add(throwEvent);
        host.Children.Add(afterThrow);
        host.Children.Add(hostEnd);
        host.ChildFlows.Add(new() { Source = hostStart, Target = setup });
        host.ChildFlows.Add(new() { Source = setup, Target = throwEvent });
        host.ChildFlows.Add(new() { Source = throwEvent, Target = afterThrow });
        host.ChildFlows.Add(new() { Source = afterThrow, Target = hostEnd });
        var eventSub = new EventSubProcess { Name = "escalation-sub" };
        var eventStart = new FlowEvent {
            Name = "escalation-start", Position = EventPosition.Start, Definition = escalation, Interrupting = false,
        };
        var eventHandler = new NoneTask { Name = "event-handler" };
        var eventEnd = new FlowEvent { Name = "event-end", Position = EventPosition.End };
        eventSub.Children.Add(eventStart);
        eventSub.Children.Add(eventHandler);
        eventSub.Children.Add(eventEnd);
        eventSub.ChildFlows.Add(new() { Source = eventStart, Target = eventHandler });
        eventSub.ChildFlows.Add(new() { Source = eventHandler, Target = eventEnd });
        return new() {
            Name = "derived-escalation",
            Elements = { start, rootTask, host, rootEnd, eventSub },
            Flows = {
                new() { Source = start, Target = rootTask },
                new() { Source = rootTask, Target = host },
                new() { Source = host, Target = rootEnd },
            },
        };
    }

    private static DerivedProcess NewProcess(string definitionName) {
        return new() {
            Name = "p1",
            CanonicalName = "processes/p1",
            DefinitionName = definitionName,
        };
    }

    private static FlowExecutionContext Context(List<SchemataProcessToken> staged) {
        return new(Mock.Of<IUnitOfWork>(), new ServiceCollection().BuildServiceProvider()) {
            CreateProcessAsync = (entity, _) => {
                Schemata.Flow.Tests.FlowTestCreation.Assign(entity);
                return Task.CompletedTask;
            },
            CreateTokenAsync = (entity, _) => {
                staged.Add(entity);
                Schemata.Flow.Tests.FlowTestCreation.Assign(entity);
                return Task.CompletedTask;
            },
            PersistSnapshotAsync = (_, _) => Task.CompletedTask,
        };
    }
}

[Schemata.Abstractions.Entities.CanonicalName("processes/{process}")]
public sealed class DerivedProcess : SchemataProcess;

[Schemata.Abstractions.Entities.CanonicalName("processes/{process}/tokens/{token}")]
public sealed class DerivedToken : SchemataProcessToken;

[Schemata.Abstractions.Entities.CanonicalName("processes/{process}/transitions/{transition}")]
public sealed class DerivedTransition : SchemataProcessTransition;
