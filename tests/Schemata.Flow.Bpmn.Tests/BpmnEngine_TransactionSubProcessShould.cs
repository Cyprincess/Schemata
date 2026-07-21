using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Bpmn.Runtime.Compensation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class BpmnEngine_TransactionSubProcessShould
{
    [Fact]
    public async Task Execute_TransactionWithNormalEnd_CompletesAndClearsStack() {
        var scenario = TransactionScenario(false, true, false);
        var snapshot = await RunToTransactionExitAsync(scenario);

        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Compensate);
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Cancel);

        var parent = snapshot.Tokens.Single(t => t.CanonicalName == scenario.ParentCanonical(snapshot));
        Assert.Equal("after-tx", parent.StateName);
        Assert.Equal("Active", parent.State);
    }

    [Fact]
    public async Task Execute_TransactionWithCancelEnd_InvokesAllCompensationHandlersInReverseOrder() {
        var scenario = TransactionScenario(false, true, true);
        var snapshot = await RunToTransactionExitAsync(scenario);

        var compensations = snapshot.Transitions.Where(t => t.Kind == TransitionKind.Compensate).ToList();
        Assert.Equal(2, compensations.Count);
        Assert.Equal(["B", "A"], compensations.Select(t => t.Previous));
        Assert.Equal(["undo-B", "undo-A"], compensations.Select(t => t.Posterior));
    }

    [Fact]
    public async Task Execute_TransactionWithCancelEndAndCancelBoundary_ActivatesBoundaryAfterCompensation() {
        var scenario = TransactionScenario(true, true, true);
        var snapshot = await RunToTransactionExitAsync(scenario);

        var rows = snapshot.Transitions.ToList();
        var compensationIndexes = rows.Select((transition, index) => (transition, index))
                                      .Where(item => item.transition.Kind == TransitionKind.Compensate)
                                      .Select(item => item.index)
                                      .ToList();
        var boundarySpawn = Assert.Single(rows.Select((transition, index) => (transition, index)), item =>
            item.transition is { Kind: TransitionKind.Spawn, Previous: "cancel-boundary", Posterior: "cancel-handler" });

        Assert.Equal(2, compensationIndexes.Count);
        Assert.All(compensationIndexes, index => Assert.True(index < boundarySpawn.index));
    }

    [Fact]
    public async Task Execute_TransactionWithCancelEndCancelsRemainingTokens() {
        var scenario = ParallelCancelScenario();
        var snapshot = await scenario.Engine.StartAsync(scenario.Definition, scenario.Process, CancellationToken.None);
        var first = snapshot.Tokens.Single(t => t.StateName == "first");
        snapshot = await scenario.Engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, first.CanonicalName, CancellationToken.None);
        var second = snapshot.Tokens.Single(t => t.StateName == "second");
        snapshot = await scenario.Engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, second.CanonicalName, CancellationToken.None);
        var cancelBranch = snapshot.Tokens.Single(t => t.StateName == "cancel-task");

        snapshot = await scenario.Engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, cancelBranch.CanonicalName, CancellationToken.None);

        var other = snapshot.Tokens.Single(t => t.StateName == "slow-task");
        Assert.Equal("Cancelled", other.State);
        Assert.Contains(snapshot.Transitions, t => t.Kind == TransitionKind.Cancel && t.Token == other.CanonicalName);
        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Cancel && t.Token == cancelBranch.CanonicalName);
    }

    [Fact]
    public async Task Execute_TransactionWithCompensationHandlerThrows_PropagatesViaErrorBoundary() {
        var scenario = TransactionScenario(false, true, true, true);
        var beforeCancel = await RunToBeforeCancelEndAsync(scenario);
        RegisterHandler(scenario.Engine, scenario.ParentCanonical(beforeCancel), new ThrowingHandler(scenario.B, new InvalidOperationException("boom")));

        var snapshot = await scenario.Engine.AdvanceAsync(
            scenario.Definition,
            beforeCancel.Process,
            beforeCancel.Tokens,
            beforeCancel.Tokens.Single(t => t.StateName == "cancel-task").CanonicalName,
            CancellationToken.None);

        Assert.Single(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "error-boundary", Posterior: "error-handler" });
        Assert.Equal("Running", snapshot.Process.State);
    }

    [Fact]
    public async Task Execute_TransactionWithCompensationHandlerThrowsNoErrorBoundary_PropagatesOutward() {
        var scenario = TransactionScenario(false, true, true);
        var beforeCancel = await RunToBeforeCancelEndAsync(scenario);
        var failure = new InvalidOperationException("boom");
        RegisterHandler(scenario.Engine, scenario.ParentCanonical(beforeCancel), new ThrowingHandler(scenario.B, failure));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scenario.Engine.AdvanceAsync(
                scenario.Definition,
                beforeCancel.Process,
                beforeCancel.Tokens,
                beforeCancel.Tokens.Single(t => t.StateName == "cancel-task").CanonicalName,
                CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Equal("Failed", beforeCancel.Process.State);
    }

    [Fact]
    public async Task Execute_TransactionWithoutCompensationBoundaries_CancelEndStillFiresCancelBoundary() {
        var scenario = TransactionScenario(true, false, true);
        var snapshot = await RunToTransactionExitAsync(scenario);

        Assert.DoesNotContain(snapshot.Transitions, t => t.Kind == TransitionKind.Compensate);
        Assert.Single(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "cancel-boundary", Posterior: "cancel-handler" });
    }

    [Fact]
    public async Task Execute_TransactionNormalEnd_DoesNotActivateCancelBoundary() {
        var scenario = TransactionScenario(true, true, false);
        var snapshot = await RunToTransactionExitAsync(scenario);

        Assert.DoesNotContain(snapshot.Transitions, t => t is { Kind: TransitionKind.Spawn, Previous: "cancel-boundary" });
        Assert.Equal("after-tx", snapshot.Tokens.Single(t => t.CanonicalName == scenario.ParentCanonical(snapshot)).StateName);
    }

    private static async Task<ProcessSnapshot> RunToTransactionExitAsync(TransactionScenarioData scenario) {
        var snapshot = await RunToBeforeCancelEndAsync(scenario);
        var last = snapshot.Tokens.Single(t => t.StateName == (scenario.CancelEnd ? "cancel-task" : "B"));
        return await scenario.Engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, last.CanonicalName, CancellationToken.None);
    }

    private static async Task<ProcessSnapshot> RunToBeforeCancelEndAsync(TransactionScenarioData scenario) {
        var snapshot = await scenario.Engine.StartAsync(scenario.Definition, scenario.Process, CancellationToken.None);
        var first = snapshot.Tokens.Single(t => t.StateName == "A");
        snapshot = await scenario.Engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, first.CanonicalName, CancellationToken.None);
        var second = snapshot.Tokens.Single(t => t.StateName == "B");
        if (scenario.CancelEnd) {
            snapshot = await scenario.Engine.AdvanceAsync(scenario.Definition, snapshot.Process, snapshot.Tokens, second.CanonicalName, CancellationToken.None);
            Assert.Single(snapshot.Tokens, t => t.StateName == "cancel-task");
        }

        return snapshot;
    }

    private static TransactionScenarioData TransactionScenario(
        bool cancelBoundary,
        bool compensationBoundaries,
        bool cancelEnd,
        bool errorBoundary = false
    ) {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var tx    = new TransactionSubProcess { Name = "tx" };
        var after = new NoneTask { Name = "after-tx" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };

        var txStart = new FlowEvent { Name = "tx-start", Position = EventPosition.Start };
        var a       = new NoneTask { Name = "A" };
        var b       = new NoneTask { Name = "B" };
        var normal  = new FlowEvent { Name = "tx-end", Position = EventPosition.End };
        var cancelTask = new NoneTask { Name = "cancel-task" };
        var cancelEndEvent = new FlowEvent {
            Name         = "cancel-end",
            Position   = EventPosition.End,
            Definition = new CancelDefinition { Name = "Cancel" },
        };

        tx.Children.Add(txStart);
        tx.Children.Add(a);
        tx.Children.Add(b);
        if (cancelEnd) {
            tx.Children.Add(cancelTask);
            tx.Children.Add(cancelEndEvent);
            tx.ChildFlows.Add(new() { Source = b, Target = cancelTask });
            tx.ChildFlows.Add(new() { Source = cancelTask, Target = cancelEndEvent });
        } else {
            tx.Children.Add(normal);
            tx.ChildFlows.Add(new() { Source = b, Target = normal });
        }
        tx.ChildFlows.Add(new() { Source = txStart, Target = a });
        tx.ChildFlows.Add(new() { Source = a, Target = b });

        var definition = new ProcessDefinition {
            Name     = $"transaction-{Guid.NewGuid():N}",
            Elements = { start, tx, after, end },
            Flows = {
                new() { Source = start, Target = tx },
                new() { Source = tx, Target = after },
                new() { Source = after, Target = end },
            },
        };

        if (compensationBoundaries) {
            AddCompensation(definition, a, "A");
            AddCompensation(definition, b, "B");
        }

        if (cancelBoundary) {
            var boundary = new FlowEvent {
                Name           = "cancel-boundary",
                Position     = EventPosition.Boundary,
                AttachedTo   = tx,
                Interrupting = true,
                Definition   = new CancelDefinition { Name = "Cancel" },
            };
            var handler = new NoneTask { Name = "cancel-handler" };
            definition.Elements.Add(boundary);
            definition.Elements.Add(handler);
            definition.Flows.Add(new() { Source = boundary, Target = handler });
        }

        if (errorBoundary) {
            var boundary = new FlowEvent {
                Name           = "error-boundary",
                Position     = EventPosition.Boundary,
                AttachedTo   = tx,
                Interrupting = true,
                Definition   = new ErrorDefinition { Name = "Error", ExceptionType = typeof(InvalidOperationException) },
            };
            var handler = new NoneTask { Name = "error-handler" };
            definition.Elements.Add(boundary);
            definition.Elements.Add(handler);
            definition.Flows.Add(new() { Source = boundary, Target = handler });
        }

        return new(new(), definition, NewProcess(definition.Name), tx, a, b, cancelEnd);
    }

    private static TransactionScenarioData ParallelCancelScenario() {
        var start = new FlowEvent { Name = "start", Position = EventPosition.Start };
        var tx    = new TransactionSubProcess { Name = "tx" };
        var after = new NoneTask { Name = "after-tx" };
        var end   = new FlowEvent { Name = "end", Position = EventPosition.End };

        var txStart = new FlowEvent { Name = "tx-start", Position = EventPosition.Start };
        var first   = new NoneTask { Name = "first" };
        var second  = new NoneTask { Name = "second" };
        var fork    = new ParallelGateway { Name = "fork" };
        var cancelTask = new NoneTask { Name = "cancel-task" };
        var slowTask   = new NoneTask { Name = "slow-task" };
        var cancelEnd = new FlowEvent {
            Name         = "cancel-end",
            Position   = EventPosition.End,
            Definition = new CancelDefinition { Name = "Cancel" },
        };
        var slowEnd = new FlowEvent { Name = "slow-end", Position = EventPosition.End };

        tx.Children.Add(txStart);
        tx.Children.Add(first);
        tx.Children.Add(second);
        tx.Children.Add(fork);
        tx.Children.Add(cancelTask);
        tx.Children.Add(slowTask);
        tx.Children.Add(cancelEnd);
        tx.Children.Add(slowEnd);
        tx.ChildFlows.Add(new() { Source = txStart, Target = first });
        tx.ChildFlows.Add(new() { Source = first, Target = second });
        tx.ChildFlows.Add(new() { Source = second, Target = fork });
        tx.ChildFlows.Add(new() { Source = fork, Target = cancelTask });
        tx.ChildFlows.Add(new() { Source = fork, Target = slowTask });
        tx.ChildFlows.Add(new() { Source = cancelTask, Target = cancelEnd });
        tx.ChildFlows.Add(new() { Source = slowTask, Target = slowEnd });

        var definition = new ProcessDefinition {
            Name     = $"transaction-parallel-{Guid.NewGuid():N}",
            Elements = { start, tx, after, end },
            Flows = {
                new() { Source = start, Target = tx },
                new() { Source = tx, Target = after },
                new() { Source = after, Target = end },
            },
        };

        return new(new(), definition, NewProcess(definition.Name), tx, first, second, true);
    }

    private static void AddCompensation(ProcessDefinition definition, Activity activity, string suffix) {
        var boundary = new FlowEvent {
            Name         = $"compensate-{suffix}",
            Position   = EventPosition.Boundary,
            AttachedTo = activity,
            Definition = new CompensationDefinition { Name = $"compensate-{suffix}", Activity = activity },
        };
        var target = new NoneTask { Name = $"undo-{suffix}" };
        definition.Elements.Add(boundary);
        definition.Elements.Add(target);
        definition.Flows.Add(new() { Source = boundary, Target = target });
    }

    private static void RegisterHandler(BpmnEngine engine, string scopeOwner, ICompensationHandler handler) {
        var ensure = typeof(BpmnEngine).GetMethod(
            "EnsureCompensationScope",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(ensure);

        var stack = Assert.IsType<CompensationStack>(ensure.Invoke(engine, [scopeOwner]));
        stack.Register(handler);
    }

    private static SchemataProcess NewProcess(string definitionName) {
        return new() {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definitionName,
        };
    }

    private sealed record TransactionScenarioData(
        BpmnEngine            Engine,
        ProcessDefinition     Definition,
        SchemataProcess       Process,
        TransactionSubProcess Transaction,
        Activity              A,
        Activity              B,
        bool                  CancelEnd)
    {
        public string ParentCanonical(ProcessSnapshot snapshot) {
            return snapshot.Tokens.Single(t => t.StateName == Transaction.Name || t.CanonicalName == snapshot.Tokens.First(x => x.Spawner is not null && x.ScopeName == Transaction.Name).Spawner).CanonicalName!;
        }
    }

    private sealed class ThrowingHandler(Activity activity, Exception failure) : ICompensationHandler
    {
        public Activity Activity { get; } = activity;

        public FlowElement CompensationTarget { get; } = new NoneTask { Name = "throwing-target" };

        public ValueTask InvokeAsync(CompensationInvocationContext context, CancellationToken ct = default) {
            context.Transitions.Add(new() {
                Name      = Guid.NewGuid().ToString("n"),
                Process   = context.Process.Name!,
                Token     = context.Scope.CanonicalName,
                Previous  = Activity.Name,
                Posterior = CompensationTarget.Name,
                Kind      = TransitionKind.Compensate,
                Event     = "ThrowingHandler",
            });
            throw failure;
        }
    }
}
