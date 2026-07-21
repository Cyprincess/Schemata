using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Exceptions;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngineInclusiveShould
{
    [Fact]
    public async Task InclusiveBranch_AllConditionsTrue_SpawnsAllBranches() {
        var definition = InclusiveBranchDefinition(true, true, true);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var routed  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        var active = routed.Tokens.Where(t => t.State == "Active").Select(t => t.StateName).OrderBy(s => s).ToList();
        Assert.Equal(new[] { "task-a", "task-b", "task-c" }, active);
    }

    [Fact]
    public async Task InclusiveBranch_SingleConditionTrue_SpawnsOneBranchOnly() {
        var definition = InclusiveBranchDefinition(false, true, false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var routed  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        var active = routed.Tokens.Where(t => t.State == "Active").Select(t => t.StateName).ToList();
        Assert.Single(active);
        Assert.Equal("task-b", active[0]);
    }

    [Fact]
    public async Task InclusiveBranch_AllConditionsFalseWithDefault_TakesDefault() {
        var definition = InclusiveBranchWithDefault(false, false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var started = await engine.StartAsync(definition, process, CancellationToken.None);
        var routed  = await engine.AdvanceAsync(definition, started.Process, started.Tokens, null, CancellationToken.None);

        var active = routed.Tokens.Where(t => t.State == "Active").ToList();
        Assert.Single(active);
        Assert.Equal("task-default", active[0].StateName);
    }

    [Fact]
    public async Task InclusiveMerge_AllSpawnedTokensArrived_FiresMerge() {
        var definition = InclusiveBranchMergeDefinition(true, true, true);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);

        var taskA = snapshot.Tokens.First(t => t.StateName == "task-a");
        var taskB = snapshot.Tokens.First(t => t.StateName == "task-b");
        var taskC = snapshot.Tokens.First(t => t.StateName == "task-c");

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskA.CanonicalName, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskB.CanonicalName, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskC.CanonicalName, CancellationToken.None);

        Assert.Contains(snapshot.Transitions, t => t.Kind == TransitionKind.Join);
        Assert.NotNull(snapshot.Tokens.FirstOrDefault(t => t.StateName == "after"));
    }

    [Fact]
    public async Task InclusiveMerge_DeadPath_FiresWithFewerArrivalsThanIncomingCount() {
        var definition = InclusiveBranchMergeDefinition(true, true, false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);

        var taskA = snapshot.Tokens.First(t => t.StateName == "task-a");
        var taskB = snapshot.Tokens.First(t => t.StateName == "task-b");

        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskA.CanonicalName, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskB.CanonicalName, CancellationToken.None);

        Assert.Contains(snapshot.Transitions, t => t.Kind == TransitionKind.Join);
        Assert.NotNull(snapshot.Tokens.FirstOrDefault(t => t.StateName == "after"));
    }

    [Fact]
    public async Task InclusiveMerge_LiveUpstreamRemaining_KeepsCurrentWaiting() {
        var definition = InclusiveBranchMergeDefinition(true, true, true);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);

        var taskA = snapshot.Tokens.First(t => t.StateName == "task-a");
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskA.CanonicalName, CancellationToken.None);

        Assert.Single(snapshot.Tokens, t => t.State == "Waiting");
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Join);
    }

    [Fact]
    public async Task InclusiveMerge_OnlyDeadPaths_AllArrivedExceptOne_FiresOnLastArrival() {
        var definition = InclusiveBranchMergeDefinition(true, false, false);
        var process    = NewProcess(definition.Name);
        var engine     = new BpmnEngine();

        var snapshot = await engine.StartAsync(definition, process, CancellationToken.None);
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, null, CancellationToken.None);

        var taskA = snapshot.Tokens.First(t => t.StateName == "task-a");
        snapshot = await engine.AdvanceAsync(definition, snapshot.Process, snapshot.Tokens, taskA.CanonicalName, CancellationToken.None);

        Assert.Contains(snapshot.Transitions, t => t.Kind == TransitionKind.Join);
    }

    [Fact]
    public void BpmnValidator_InclusiveGatewayWithoutOutgoing_Throws() {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var gateway  = new InclusiveGateway { Name = "split" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };
        var definition = new ProcessDefinition {
            Name     = "broken-inclusive",
            Elements = { start, gateway, endEvent },
            Flows    = { new() { Source = start, Target = gateway } },
        };

        Assert.Throws<FailedPreconditionException>(() => BpmnValidator.Validate(definition));
    }

    private static ProcessDefinition InclusiveBranchDefinition(bool condA, bool condB, bool condC) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var src   = new NoneTask { Name = "src" };
        var split = new InclusiveGateway { Name = "split" };
        var a     = new NoneTask { Name = "task-a" };
        var b     = new NoneTask { Name = "task-b" };
        var c     = new NoneTask { Name = "task-c" };
        var endA  = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB  = new FlowEvent { Name = "end-b", Position = EventPosition.End };
        var endC  = new FlowEvent { Name = "end-c", Position = EventPosition.End };

        return new() {
            Name     = "inclusive-branch",
            Elements = { start, src, split, a, b, c, endA, endB, endC },
            Flows = {
                new() { Source = start, Target = src },
                new() { Source = src, Target = split },
                new() { Source = split, Target = a, Condition = Const(condA) },
                new() { Source = split, Target = b, Condition = Const(condB) },
                new() { Source = split, Target = c, Condition = Const(condC) },
                new() { Source = a, Target = endA },
                new() { Source = b, Target = endB },
                new() { Source = c, Target = endC },
            },
        };
    }

    private static ProcessDefinition InclusiveBranchWithDefault(bool condA, bool condB) {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var src      = new NoneTask { Name = "src" };
        var split    = new InclusiveGateway { Name = "split" };
        var a        = new NoneTask { Name = "task-a" };
        var b        = new NoneTask { Name = "task-b" };
        var fallback = new NoneTask { Name = "task-default" };
        var endA     = new FlowEvent { Name = "end-a", Position = EventPosition.End };
        var endB     = new FlowEvent { Name = "end-b", Position = EventPosition.End };
        var endD     = new FlowEvent { Name = "end-d", Position = EventPosition.End };

        return new() {
            Name     = "inclusive-default",
            Elements = { start, src, split, a, b, fallback, endA, endB, endD },
            Flows = {
                new() { Source = start, Target = src },
                new() { Source = src, Target = split },
                new() { Source = split, Target = a, Condition = Const(condA) },
                new() { Source = split, Target = b, Condition = Const(condB) },
                new() { Source = split, Target = fallback, IsDefault = true },
                new() { Source = a, Target = endA },
                new() { Source = b, Target = endB },
                new() { Source = fallback, Target = endD },
            },
        };
    }

    private static ProcessDefinition InclusiveBranchMergeDefinition(bool condA, bool condB, bool condC) {
        var start    = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var src      = new NoneTask { Name = "src" };
        var split    = new InclusiveGateway { Name = "split" };
        var a        = new NoneTask { Name = "task-a" };
        var b        = new NoneTask { Name = "task-b" };
        var c        = new NoneTask { Name = "task-c" };
        var merge    = new InclusiveGateway { Name = "merge" };
        var after    = new NoneTask { Name = "after" };
        var endEvent = new FlowEvent { Name = "end", Position = EventPosition.End };

        return new() {
            Name     = "inclusive-branch-merge",
            Elements = { start, src, split, a, b, c, merge, after, endEvent },
            Flows = {
                new() { Source = start, Target = src },
                new() { Source = src, Target = split },
                new() { Source = split, Target = a, Condition = Const(condA) },
                new() { Source = split, Target = b, Condition = Const(condB) },
                new() { Source = split, Target = c, Condition = Const(condC) },
                new() { Source = a, Target = merge },
                new() { Source = b, Target = merge },
                new() { Source = c, Target = merge },
                new() { Source = merge, Target = after },
                new() { Source = after, Target = endEvent },
            },
        };
    }

    private static IConditionExpression Const(bool value) {
        return new LambdaConditionExpression { Lambda = _ => new(value) };
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }
}
