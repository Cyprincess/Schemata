using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_EventSubProcessShould
{
    [Fact]
    public async Task Trigger_InterruptingEventSubProcess_CancelsAllParentScopeTokens() {
        var scenario = InterruptingScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var token    = started.Tokens.Single(t => t.StateName == "task-a");
        var fired = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            token.CanonicalName,
            CancellationToken.None);
        var parentTokens = fired.Tokens.Where(t => t.ScopeName == started.Process.Name).ToList();
        Assert.Equal(2, parentTokens.Count);
        Assert.All(parentTokens, parent => Assert.Equal("Cancelled", parent.State));
        var cancelRows = fired.Transitions.Where(t => t.Kind == TransitionKind.Cancel).ToList();
        Assert.Equal(2, cancelRows.Count);
        Assert.All(cancelRows, row => Assert.Equal(scenario.Trigger.Name, row.Event));
        var child = Assert.Single(fired.Tokens, t => t.ScopeName == "event-sub");
        Assert.Equal("event-task", child.StateName);
        var spawn = Assert.Single(fired.Transitions, t => t.Kind == TransitionKind.Spawn);
        Assert.Equal(child.CanonicalName, spawn.Token);
    }

    [Fact]
    public async Task Trigger_InterruptingEventSubProcess_SpawnedTokenStartsAtInnerStartEventOutgoing() {
        var scenario = InterruptingScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var token    = started.Tokens.Single(t => t.StateName == "task-a");
        var fired = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            token.CanonicalName,
            CancellationToken.None);
        var child = Assert.Single(fired.Tokens, t => t.ScopeName == "event-sub");
        Assert.Equal("event-task", child.StateName);
        Assert.Null(child.WaitingAtName);
        Assert.Equal("Active", child.State);
    }

    [Fact]
    public async Task Trigger_InterruptingEventSubProcess_CompletionAdvancesParentScopeToExit() {
        var scenario = InterruptingScenario();
        var engine   = new BpmnEngine();
        var snapshot = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var token    = snapshot.Tokens.Single(t => t.StateName == "task-a");
        snapshot = await engine.TriggerAsync(
            scenario.Definition,
            snapshot.Process,
            snapshot.Tokens,
            scenario.Trigger,
            null,
            token.CanonicalName,
            CancellationToken.None);
        var child = snapshot.Tokens.Single(t => t.ScopeName == "event-sub");
        snapshot = await engine.AdvanceAsync(
            scenario.Definition,
            snapshot.Process,
            snapshot.Tokens,
            child.CanonicalName,
            CancellationToken.None);
        Assert.Equal("Completed", snapshot.Process.State);
        Assert.All(snapshot.Tokens, tokenSnapshot => Assert.Contains(tokenSnapshot.State, new[] { "Cancelled", "Completed" }));
    }

    [Fact]
    public async Task Trigger_NonInterruptingEventSubProcess_LeavesParentTokensActive() {
        var scenario = NonInterruptingScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var token    = started.Tokens.Single(t => t.StateName == "task-a");
        var fired = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            token.CanonicalName,
            CancellationToken.None);
        var parentTokens = fired.Tokens.Where(t => t.ScopeName == started.Process.Name).ToList();
        Assert.Equal(2, parentTokens.Count);
        Assert.All(parentTokens, parent => Assert.Equal("Active", parent.State));
        Assert.DoesNotContain(fired.Transitions, t => t.Kind == TransitionKind.Cancel);
        var child = Assert.Single(fired.Tokens, t => t.ScopeName == "event-sub");
        Assert.Equal("event-task", child.StateName);
        var spawn = Assert.Single(fired.Transitions, t => t.Kind == TransitionKind.Spawn);
        Assert.Equal(child.CanonicalName, spawn.Token);
    }

    [Fact]
    public async Task Trigger_NonInterruptingEventSubProcess_ParentCompletesIndependentlyOfChild() {
        var scenario = NonInterruptingScenario();
        var engine   = new BpmnEngine();
        var snapshot = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var token    = snapshot.Tokens.Single(t => t.StateName == "task-a");
        snapshot = await engine.TriggerAsync(
            scenario.Definition,
            snapshot.Process,
            snapshot.Tokens,
            scenario.Trigger,
            null,
            token.CanonicalName,
            CancellationToken.None);
        var child = snapshot.Tokens.Single(t => t.ScopeName == "event-sub");
        foreach (var parent in snapshot.Tokens.Where(t => t.ScopeName == snapshot.Process.Name).ToList()) {
            snapshot = await engine.AdvanceAsync(
                scenario.Definition,
                snapshot.Process,
                snapshot.Tokens,
                parent.CanonicalName,
                CancellationToken.None);
        }
        Assert.Equal("Running", snapshot.Process.State);
        Assert.Equal("Active", snapshot.Tokens.Single(t => t.CanonicalName == child.CanonicalName).State);
        snapshot = await engine.AdvanceAsync(
            scenario.Definition,
            snapshot.Process,
            snapshot.Tokens,
            child.CanonicalName,
            CancellationToken.None);
        Assert.Equal("Completed", snapshot.Process.State);
    }

    [Fact]
    public async Task Trigger_NonInterruptingEventSubProcess_ChildCompletesIndependentlyOfParent() {
        var scenario = NonInterruptingScenario();
        var engine   = new BpmnEngine();
        var snapshot = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var token    = snapshot.Tokens.Single(t => t.StateName == "task-a");
        snapshot = await engine.TriggerAsync(
            scenario.Definition,
            snapshot.Process,
            snapshot.Tokens,
            scenario.Trigger,
            null,
            token.CanonicalName,
            CancellationToken.None);
        var child = snapshot.Tokens.Single(t => t.ScopeName == "event-sub");
        snapshot = await engine.AdvanceAsync(
            scenario.Definition,
            snapshot.Process,
            snapshot.Tokens,
            child.CanonicalName,
            CancellationToken.None);
        Assert.Equal("Running", snapshot.Process.State);
        Assert.Equal("Completed", snapshot.Tokens.Single(t => t.CanonicalName == child.CanonicalName).State);
        foreach (var parent in snapshot.Tokens.Where(t => t.ScopeName == snapshot.Process.Name).ToList()) {
            snapshot = await engine.AdvanceAsync(
                scenario.Definition,
                snapshot.Process,
                snapshot.Tokens,
                parent.CanonicalName,
                CancellationToken.None);
        }
        Assert.Equal("Completed", snapshot.Process.State);
    }

    [Fact]
    public async Task Trigger_EventSubProcessWithBoundaryHigherPriority_BoundaryWinsOnHostActivity() {
        var scenario = BoundaryPriorityScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var host     = started.Tokens.Single(t => t.StateName == "host");
        var fired = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            host.CanonicalName,
            CancellationToken.None);
        Assert.Equal("Cancelled", fired.Tokens.Single(t => t.CanonicalName == host.CanonicalName).State);
        Assert.Single(fired.Tokens, t => t.StateName == "boundary-handler");
        Assert.DoesNotContain(fired.Tokens, t => t.StateName == "event-task");
        Assert.Contains(fired.Transitions, t => t is { Kind: TransitionKind.Cancel, Previous: "host", Posterior: "boundary" });
        Assert.DoesNotContain(fired.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "event-sub" });
    }

    private static EventSubProcessScenario InterruptingScenario() {
        return ParallelParentScenario(new Signal { Name = "scope-signal" }, true, "interrupting-event-subprocess");
    }

    private static EventSubProcessScenario NonInterruptingScenario() {
        return ParallelParentScenario(new Signal { Name = "scope-signal" }, false, "non-interrupting-event-subprocess");
    }

    private static EventSubProcessScenario ParallelParentScenario(IEventDefinition trigger, bool interrupting, string definitionName) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var fork  = new ParallelGateway { Name = "fork" };
        var taskA = new NoneTask { Name = "task-a" };
        var taskB = new NoneTask { Name = "task-b" };
        var endA  = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB  = new FlowEvent { Name = "end-b", Position = EventPosition.End };
        var eventSub = EventSubProcess(trigger, interrupting);
        var definition = new ProcessDefinition {
            Name     = definitionName,
            Elements = { start, fork, taskA, taskB, endA, endB, eventSub },
            Flows = {
                new() { Source = start, Target = fork },
                new() { Source = fork, Target = taskA },
                new() { Source = fork, Target = taskB },
                new() { Source = taskA, Target = endA },
                new() { Source = taskB, Target = endB },
            },
        };
        return new(definition, trigger);
    }

    private static EventSubProcessScenario BoundaryPriorityScenario() {
        var trigger = new Signal { Name = "shared-signal" };
        var start   = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var host    = new NoneTask { Name = "host" };
        var boundary = new FlowEvent {
            Name           = "boundary",
            Position     = EventPosition.Boundary,
            AttachedTo   = host,
            Interrupting = true,
            Definition   = trigger,
        };
        var boundaryHandler = new NoneTask { Name = "boundary-handler" };
        var normalEnd       = new FlowEvent { Name = "normal-end", Position = EventPosition.End };
        var boundaryEnd     = new FlowEvent { Name = "boundary-end", Position = EventPosition.End };
        var eventSub        = EventSubProcess(trigger, true);
        var definition = new ProcessDefinition {
            Name     = "boundary-priority-event-subprocess",
            Elements = { start, host, boundary, boundaryHandler, normalEnd, boundaryEnd, eventSub },
            Flows = {
                new() { Source = start, Target = host },
                new() { Source = host, Target = normalEnd },
                new() { Source = boundary, Target = boundaryHandler },
                new() { Source = boundaryHandler, Target = boundaryEnd },
            },
        };
        return new(definition, trigger);
    }

    private static EventSubProcess EventSubProcess(IEventDefinition trigger, bool interrupting) {
        var eventSub   = new EventSubProcess { Name = "event-sub" };
        var innerStart = new FlowEvent {
            Name           = "event-start",
            Position     = EventPosition.Start,
            Definition   = trigger,
            Interrupting = interrupting,
        };
        var eventTask = new NoneTask { Name = "event-task" };
        var eventEnd  = new FlowEvent { Name = "event-end", Position = EventPosition.End };
        eventSub.Children.Add(innerStart);
        eventSub.Children.Add(eventTask);
        eventSub.Children.Add(eventEnd);
        eventSub.ChildFlows.Add(new() { Source = innerStart, Target = eventTask });
        eventSub.ChildFlows.Add(new() { Source = eventTask, Target = eventEnd });
        return eventSub;
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }

    private sealed record EventSubProcessScenario(ProcessDefinition Definition, IEventDefinition Trigger);
}
