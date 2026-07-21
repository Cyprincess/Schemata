using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngineParallelShould
{
    [Fact]
    public async Task Fork_OneInputThreeOutgoing_SpawnsThreeActiveChildrenAndCompletesInput() {
        var definition = ForkDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var forked  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        Assert.Equal(4, forked.Tokens.Count);

        var completed = forked.Tokens.Where(t => t.State == "Completed").ToList();
        Assert.Single(completed);

        var active = forked.Tokens.Where(t => t.State == "Active").ToList();
        Assert.Equal(3, active.Count);
        Assert.Equal(["task-a", "task-b", "task-c"], active.Select(t => t.StateName).OrderBy(s => s));

        Assert.Equal("Running", forked.Process.State);
        Assert.Contains(forked.Transitions, t => t.Kind == TransitionKind.Fork);
        Assert.Equal(3, forked.Transitions.Count(t => t.Kind == TransitionKind.Move));
    }

    [Fact]
    public async Task Join_AllSiblingsArrived_CollapsesToSingleOutputToken() {
        var definition = ForkJoinDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);

        var taskAToken = snapshot.Tokens.First(t => t.StateName == "task-a");
        var taskBToken = snapshot.Tokens.First(t => t.StateName == "task-b");
        var taskCToken = snapshot.Tokens.First(t => t.StateName == "task-c");

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskAToken.CanonicalName, CancellationToken.None);
        Assert.Equal("Running", snapshot.Process.State);
        Assert.Single(snapshot.Tokens, t => t.State == "Waiting");

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskBToken.CanonicalName, CancellationToken.None);
        Assert.Equal("Running", snapshot.Process.State);
        Assert.Equal(2, snapshot.Tokens.Count(t => t.State == "Waiting"));

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskCToken.CanonicalName, CancellationToken.None);

        Assert.DoesNotContain(snapshot.Tokens, t => t.State == "Waiting");
        Assert.Contains(snapshot.Transitions, t => t.Kind == TransitionKind.Join);

        var output = snapshot.Tokens.FirstOrDefault(t => t.StateName == "after-join");
        Assert.NotNull(output);
        Assert.Equal("Active", output!.State);

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, output.CanonicalName, CancellationToken.None);
        Assert.Equal("Completed", snapshot.Process.State);
    }

    [Fact]
    public async Task Join_FailedSibling_StillCountsTowardCollapse() {
        var definition = ForkJoinDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);

        var taskA = snapshot.Tokens.First(t => t.StateName == "task-a");
        var taskB = snapshot.Tokens.First(t => t.StateName == "task-b");
        var taskC = snapshot.Tokens.First(t => t.StateName == "task-c");

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskA.CanonicalName, CancellationToken.None);

        var workingList = snapshot.Tokens.ToList();
        var bToken      = workingList.First(t => t.CanonicalName == taskB.CanonicalName);
        bToken.State       = "Failed";
        bToken.StateName     = "join";
        bToken.WaitingAtName = "join";

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, workingList, taskC.CanonicalName, CancellationToken.None);

        Assert.Contains(snapshot.Transitions, t => t.Kind == TransitionKind.Join);
        Assert.NotNull(snapshot.Tokens.FirstOrDefault(t => t.StateName == "after-join"));
    }

    [Fact]
    public async Task Join_NotAllSiblingsArrived_LeavesAddressedTokenWaiting() {
        var definition = ForkJoinDefinition();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);

        var taskA = snapshot.Tokens.First(t => t.StateName == "task-a");
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskA.CanonicalName, CancellationToken.None);

        var waiting = Assert.Single(snapshot.Tokens, t => t.State == "Waiting");
        Assert.Equal("join", waiting.WaitingAtName);
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Join);
        Assert.Equal("Running", snapshot.Process.State);
    }

    [Fact]
    public async Task Fork_DirectlyAfterStart_SpawnsNTokensImmediately() {
        var definition = ForkDirectlyAfterStart();
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);

        Assert.Equal(2, started.Tokens.Count);
        Assert.All(started.Tokens, t => Assert.Equal("Active", t.State));
        Assert.Contains(started.Transitions, t => t.Kind == TransitionKind.Fork);
    }

    [Fact]
    public void BpmnValidator_ParallelGatewayWithoutOutgoing_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new ParallelGateway { Name = "fork" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "broken-parallel",
            Elements = { start, gateway, endEvent },
            Flows    = { new() { Source = start, Target = gateway } },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    [Fact]
    public void BpmnValidator_ParallelGatewayWithoutIncoming_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new ParallelGateway { Name = "fork" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "no-incoming-parallel",
            Elements = { start, gateway, endEvent },
            Flows = {
                new() { Source = start, Target = endEvent },
                new() { Source = gateway, Target = endEvent },
            },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    private static ProcessDefinition ForkDefinition() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var src   = new NoneTask { Name = "src" };
        var fork  = new ParallelGateway { Name = "fork" };
        var a     = new NoneTask { Name = "task-a" };
        var b     = new NoneTask { Name = "task-b" };
        var c     = new NoneTask { Name = "task-c" };
        var endA  = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB  = new FlowEvent { Name = "end-b", Position = EventPosition.End };
        var endC  = new FlowEvent { Name = "end-c", Position = EventPosition.End };

        return new() {
            Name     = "fork-three",
            Elements = { start, src, fork, a, b, c, endA, endB, endC },
            Flows = {
                new() { Source = start, Target = src },
                new() { Source = src, Target = fork },
                new() { Source = fork, Target = a },
                new() { Source = fork, Target = b },
                new() { Source = fork, Target = c },
                new() { Source = a, Target = endA },
                new() { Source = b, Target = endB },
                new() { Source = c, Target = endC },
            },
        };
    }

    private static ProcessDefinition ForkJoinDefinition() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var src      = new NoneTask { Name = "src" };
        var fork     = new ParallelGateway { Name = "fork" };
        var a        = new NoneTask { Name = "task-a" };
        var b        = new NoneTask { Name = "task-b" };
        var c        = new NoneTask { Name = "task-c" };
        var join     = new ParallelGateway { Name = "join" };
        var after    = new NoneTask { Name = "after-join" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        return new() {
            Name     = "fork-join",
            Elements = { start, src, fork, a, b, c, join, after, endEvent },
            Flows = {
                new() { Source = start, Target = src },
                new() { Source = src, Target = fork },
                new() { Source = fork, Target = a },
                new() { Source = fork, Target = b },
                new() { Source = fork, Target = c },
                new() { Source = a, Target = join },
                new() { Source = b, Target = join },
                new() { Source = c, Target = join },
                new() { Source = join, Target = after },
                new() { Source = after, Target = endEvent },
            },
        };
    }

    private static ProcessDefinition ForkDirectlyAfterStart() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var fork  = new ParallelGateway { Name = "fork" };
        var a     = new NoneTask { Name = "task-a" };
        var b     = new NoneTask { Name = "task-b" };
        var endA  = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB  = new FlowEvent { Name = "end-b", Position = EventPosition.End };

        return new() {
            Name     = "fork-after-start",
            Elements = { start, fork, a, b, endA, endB },
            Flows = {
                new() { Source = start, Target = fork },
                new() { Source = fork, Target = a },
                new() { Source = fork, Target = b },
                new() { Source = a, Target = endA },
                new() { Source = b, Target = endB },
            },
        };
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }

}
