using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Utilities;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_NonInterruptingBoundaryShould
{
    [Fact]
    public async Task Trigger_NonInterruptingBoundary_FirstFire_SpawnsSiblingAndKeepsHostActive() {
        var scenario = SignalScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var host     = started.Tokens.Single(t => t.StateName == "task");

        var fired = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            host.CanonicalName,
            CancellationToken.None);

        var stillActiveHost = fired.Tokens.Single(t => t.CanonicalName == host.CanonicalName);
        Assert.Equal("Active", stillActiveHost.State);
        Assert.Equal("task", stillActiveHost.StateName);
        Assert.Null(stillActiveHost.WaitingAtName);

        var added   = fired.Tokens.Where(t => started.Tokens.All(existing => existing.CanonicalName != t.CanonicalName)).ToList();
        var spawned = Assert.Single(added);
        Assert.Equal(host.CanonicalName, spawned.Spawner);
        Assert.Equal("Active", spawned.State);
        Assert.Equal("side-handler", spawned.StateName);
        Assert.Null(spawned.WaitingAtName);

        var spawn = Assert.Single(fired.Transitions, t => t.Kind == TransitionKind.Spawn);
        Assert.Equal(spawned.CanonicalName, spawn.Token);
        Assert.Equal("boundary", spawn.Previous);
        Assert.Equal("side-handler", spawn.Posterior);
        Assert.Equal(scenario.Trigger.Name, spawn.Event);
        Assert.DoesNotContain(fired.Transitions, t => t.Kind == TransitionKind.Cancel);
    }

    [Fact]
    public async Task Trigger_NonInterruptingBoundary_TwiceOnSameHost_SpawnsTwoSiblings() {
        var scenario = SignalScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var host     = started.Tokens.Single(t => t.StateName == "task");

        var first = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            host.CanonicalName,
            CancellationToken.None);
        var second = await engine.TriggerAsync(
            scenario.Definition,
            first.Process,
            first.Tokens,
            scenario.Trigger,
            null,
            host.CanonicalName,
            CancellationToken.None);

        var siblings = second.Tokens.Where(t => t.Spawner == host.CanonicalName).ToList();
        Assert.Equal(2, siblings.Count);
        Assert.All(siblings, sibling => Assert.Equal("Active", sibling.State));
        Assert.All(siblings, sibling => Assert.Equal("side-handler", sibling.StateName));

        var stillActiveHost = second.Tokens.Single(t => t.CanonicalName == host.CanonicalName);
        Assert.Equal("Active", stillActiveHost.State);
        Assert.Equal("task", stillActiveHost.StateName);

        var spawnRows = first.Transitions.Concat(second.Transitions).Where(t => t.Kind == TransitionKind.Spawn).ToList();
        Assert.Equal(2, spawnRows.Count);
        Assert.All(spawnRows, row => Assert.Equal("boundary", row.Previous));
    }

    [Fact]
    public async Task Trigger_NonInterruptingBoundary_SiblingCompletes_DoesNotAffectHost() {
        var scenario = SignalScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var host     = started.Tokens.Single(t => t.StateName == "task");
        var fired = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            host.CanonicalName,
            CancellationToken.None);
        var sibling = fired.Tokens.Single(t => t.Spawner == host.CanonicalName);

        var advanced = await engine.AdvanceAsync(
            scenario.Definition,
            fired.Process,
            fired.Tokens,
            sibling.CanonicalName,
            CancellationToken.None);

        var completedSibling = advanced.Tokens.Single(t => t.CanonicalName == sibling.CanonicalName);
        Assert.Equal("Completed", completedSibling.State);
        Assert.Equal("end-side", completedSibling.StateName);

        var stillActiveHost = advanced.Tokens.Single(t => t.CanonicalName == host.CanonicalName);
        Assert.Equal("Active", stillActiveHost.State);
        Assert.Equal("task", stillActiveHost.StateName);
        Assert.Equal("Running", advanced.Process.State);
    }

    [Fact]
    public async Task Trigger_NonInterruptingBoundary_HostCompletes_LeavesSiblingActive() {
        var scenario = SignalScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var host     = started.Tokens.Single(t => t.StateName == "task");
        var fired = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            host.CanonicalName,
            CancellationToken.None);
        var sibling = fired.Tokens.Single(t => t.Spawner == host.CanonicalName);

        var advanced = await engine.AdvanceAsync(
            scenario.Definition,
            fired.Process,
            fired.Tokens,
            host.CanonicalName,
            CancellationToken.None);

        var completedHost = advanced.Tokens.Single(t => t.CanonicalName == host.CanonicalName);
        Assert.Equal("Completed", completedHost.State);
        Assert.Equal("end-ok", completedHost.StateName);

        var activeSibling = advanced.Tokens.Single(t => t.CanonicalName == sibling.CanonicalName);
        Assert.Equal("Active", activeSibling.State);
        Assert.Equal("side-handler", activeSibling.StateName);
        Assert.Equal("Running", advanced.Process.State);
    }

    [Fact]
    public async Task Trigger_NonInterruptingTimerBoundary_FiresAndSpawns() {
        var scenario = TimerScenario();
        var engine   = new BpmnEngine();
        var started  = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name), CancellationToken.None);
        var host     = started.Tokens.Single(t => t.StateName == "task");

        var fired = await engine.TriggerAsync(
            scenario.Definition,
            started.Process,
            started.Tokens,
            scenario.Trigger,
            null,
            host.CanonicalName,
            CancellationToken.None);

        var stillActiveHost = fired.Tokens.Single(t => t.CanonicalName == host.CanonicalName);
        Assert.Equal("Active", stillActiveHost.State);
        Assert.Equal("task", stillActiveHost.StateName);

        var spawned = fired.Tokens.Single(t => t.Spawner == host.CanonicalName);
        Assert.Equal("Active", spawned.State);
        Assert.Equal("side-handler", spawned.StateName);

        var spawn = Assert.Single(fired.Transitions, t => t.Kind == TransitionKind.Spawn);
        Assert.Equal(spawned.CanonicalName, spawn.Token);
        Assert.Equal(scenario.Trigger.Name, spawn.Event);
        Assert.DoesNotContain(fired.Transitions, t => t.Kind == TransitionKind.Cancel);
    }

    [Trait("Layer", "Unit")]
    [Fact]
    public async Task NonInterruptingBoundary_EnteringSubProcess_StagesEachChildOnce() {
        var scenario = SignalScenario();
        var innerStart = new FlowEvent { Name = "inner-start", Position = EventPosition.Start };
        var innerTask = new NoneTask { Name = "inner-task" };
        var innerEnd = new FlowEvent { Name = "inner-end", Position = EventPosition.End };
        var sub = new EmbeddedSubProcess { Name = "side-sub", Children = { innerStart, innerTask, innerEnd }, ChildFlows = {
            new() { Source = innerStart, Target = innerTask }, new() { Source = innerTask, Target = innerEnd },
        } };
        scenario.Definition.Elements.Add(sub);
        scenario.Definition.Flows.Single(flow => flow.Source.Name == "boundary").Target = sub;
        scenario.Definition.Flows.Add(new() { Source = sub, Target = scenario.Definition.FindElementByName("end-side")! });
        var engine = new BpmnEngine();
        var snapshot = await engine.StartAsync(scenario.Definition, NewProcess(scenario.Definition.Name));
        var host = snapshot.Tokens.Single();
        snapshot = await engine.TriggerAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, scenario.Trigger, null, host.CanonicalName);
        var child = Assert.Single(snapshot.Tokens, token => token.StateName == "inner-task");
        var parent = Assert.Single(snapshot.Tokens, token => token.WaitingAtName == "side-sub");
        Assert.Equal(parent.CanonicalName, child.Spawner);
        Assert.Equal(snapshot.Tokens.Count, snapshot.Tokens.Select(token => token.CanonicalName).Distinct().Count());
        snapshot = await engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, child.CanonicalName);
        Assert.Equal("Completed", parent.State);
        Assert.Equal("Active", host.State);
        Assert.Single(snapshot.Transitions, row => row.Event == "ExitSubProcess");
    }

    private static BoundaryScenario SignalScenario() {
        return Scenario(new Signal { Name = "side-signal" }, "non-interrupting-signal");
    }

    private static BoundaryScenario TimerScenario() {
        return Scenario(
            new TimerDefinition { Name = "side-timer", TimerType = TimerType.Duration, TimeExpression = "PT5M" },
            "non-interrupting-timer");
    }

    private static BoundaryScenario Scenario(IEventDefinition trigger, string definitionName) {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var task     = new NoneTask { Name = "task" };
        var boundary = new FlowEvent {
            Name = "boundary",
            Position     = EventPosition.Boundary,
            AttachedTo   = task,
            Interrupting = false,
            Definition   = trigger,
        };
        var side      = new NoneTask { Name = "side-handler" };
        var endNormal = new FlowEvent { Name = "end-ok", Position = EventPosition.End };
        var endSide   = new FlowEvent { Name = "end-side", Position = EventPosition.End };

        var definition = new ProcessDefinition {
            Name     = definitionName,
            Elements = { start, task, boundary, side, endNormal, endSide },
            Flows = {
                new() { Source = start, Target = task },
                new() { Source = task, Target = endNormal },
                new() { Source = boundary, Target = side },
                new() { Source = side, Target = endSide },
            },
        };

        return new(definition, trigger);
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }

    private sealed record BoundaryScenario(ProcessDefinition Definition, IEventDefinition Trigger);
}
