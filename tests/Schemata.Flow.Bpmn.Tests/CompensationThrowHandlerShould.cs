using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Bpmn.Runtime;
using Schemata.Flow.Bpmn.Runtime.Compensation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class CompensationThrowHandlerShould
{
    [Fact]
    public async Task Fire_GlobalThrowOnPopulatedStack_InvokesAllInReverseOrder() {
        var calls    = new List<string>();
        var scenario = NewScenario(NewHandlers(calls));

        await new CompensationThrowHandler().FireAsync(
            scenario.Engine,
            scenario.Definition,
            scenario.Process,
            scenario.Token,
            [scenario.Token],
            new() { Name = "Global" },
            [],
            CancellationToken.None);

        Assert.Equal(new[] { "E", "D", "C", "B", "A" }, calls);
    }

    [Fact]
    public async Task Fire_GlobalThrowOnEmptyStack_IsNoOp() {
        var observer = new RecordingObserver();
        var scenario = NewScenario([]);

        var transitions = await new CompensationThrowHandler().FireAsync(
            scenario.Engine,
            scenario.Definition,
            scenario.Process,
            scenario.Token,
            [scenario.Token],
            new() { Name = "Global" },
            [observer],
            CancellationToken.None);

        Assert.Empty(transitions);
        Assert.Equal(0, observer.Started);
        Assert.Equal(0, observer.Completed);
    }

    [Fact]
    public async Task Fire_TargetedThrowForMatchingActivity_InvokesOnlyOneHandler() {
        var calls    = new List<string>();
        var handlers = NewTargetHandlers(calls);
        var scenario = NewScenario(handlers);

        var transitions = await new CompensationThrowHandler().FireAsync(
            scenario.Engine,
            scenario.Definition,
            scenario.Process,
            scenario.Token,
            [scenario.Token],
            new() { Name = "TargetY", Activity = handlers[1].Activity },
            [],
            CancellationToken.None);

        Assert.Equal(new[] { "Y" }, calls);
    }

    [Fact]
    public async Task Fire_TargetedThrowForUnknownActivity_IsNoOp() {
        var calls    = new List<string>();
        var scenario = NewScenario(NewHandlers(calls).Take(2));

        var transitions = await new CompensationThrowHandler().FireAsync(
            scenario.Engine,
            scenario.Definition,
            scenario.Process,
            scenario.Token,
            [scenario.Token],
            new() { Name = "Unknown", Activity = new NoneTask { Name = "W" } },
            [],
            CancellationToken.None);

        Assert.Empty(calls);
        Assert.Empty(transitions);
    }

    [Fact]
    public async Task Fire_GlobalThrowMidChainFails_StopsAndKeepsRemainingHandlers() {
        var calls    = new List<string>();
        var failure  = new InvalidOperationException("boom");
        var handlers = NewHandlers(calls, "C", failure);
        var scenario = NewScenario(handlers);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new CompensationThrowHandler().FireAsync(
                scenario.Engine,
                scenario.Definition,
                scenario.Process,
                scenario.Token,
                [scenario.Token],
                new() { Name = "Global" },
                [],
                CancellationToken.None));

        Assert.Same(failure, thrown);
        Assert.Equal(new[] { "E", "D" }, calls);
    }

    [Fact]
    public async Task Fire_TargetedThrowAfterInvoke_RemovesHandlerFromStack() {
        var calls    = new List<string>();
        var handlers = NewTargetHandlers(calls);
        var scenario = NewScenario(handlers);

        await new CompensationThrowHandler().FireAsync(
            scenario.Engine,
            scenario.Definition,
            scenario.Process,
            scenario.Token,
            [scenario.Token],
            new() { Name = "TargetY", Activity = handlers[1].Activity },
            [],
            CancellationToken.None);

        await new CompensationThrowHandler().FireAsync(
            scenario.Engine,
            scenario.Definition,
            scenario.Process,
            scenario.Token,
            [scenario.Token],
            new() { Name = "Global" },
            [],
            CancellationToken.None);

        Assert.Equal(new[] { "Y", "Z", "X" }, calls);
    }

    private static CompensationThrowScenario NewScenario(IEnumerable<ICompensationHandler> handlers) {
        var definition = new ProcessDefinition { Name = "throw-unit" };
        var process = new SchemataProcess {
            Name           = "p1",
            CanonicalName  = "processes/p1",
            DefinitionName = definition.Name,
        };
        var token = new SchemataProcessToken {
            Name          = "root",
            CanonicalName = "processes/p1/tokens/root",
            Process       = process.Name,
            ScopeName       = process.Name,
            StateName       = "throw",
            State         = "Active",
        };
        var engine = new BpmnEngine();
        RegisterHandlers(engine, process.CanonicalName!, handlers);
        return new(engine, definition, process, token);
    }

    private static void RegisterHandlers(BpmnEngine engine, string scopeOwner, IEnumerable<ICompensationHandler> handlers) {
        var ensure = typeof(BpmnEngine).GetMethod(
            "EnsureCompensationScope",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(ensure);

        var stack = Assert.IsType<CompensationStack>(ensure.Invoke(engine, [scopeOwner]));
        foreach (var handler in handlers) stack.Register(handler);
    }

    private static List<FakeHandler> NewTargetHandlers(List<string>? calls = null) {
        return [
            new("X", "X", calls, null),
            new("Y", "Y", calls, null),
            new("Z", "Z", calls, null),
        ];
    }
    private static List<FakeHandler> NewHandlers(
        List<string>? calls = null,
        string?       failureId = null,
        Exception?    failure = null) {
        return [
            new("A", "A", calls, failureId == "A" ? failure : null),
            new("B", "B", calls, failureId == "B" ? failure : null),
            new("C", "C", calls, failureId == "C" ? failure : null),
            new("D", "D", calls, failureId == "D" ? failure : null),
            new("E", "E", calls, failureId == "E" ? failure : null),
        ];
    }

    private sealed record CompensationThrowScenario(
        BpmnEngine            Engine,
        ProcessDefinition    Definition,
        SchemataProcess      Process,
        SchemataProcessToken Token);

    private sealed class FakeHandler : ICompensationHandler
    {
        private readonly List<string>? _calls;
        private readonly Exception?    _failure;

        public FakeHandler(string activityId, string callId, List<string>? calls, Exception? failure) {
            Activity           = new NoneTask { Name = activityId };
            CompensationTarget = new NoneTask { Name = $"compensate-{activityId}" };
            CallId             = callId;
            _calls             = calls;
            _failure           = failure;
        }

        public string CallId { get; }

        public Activity Activity { get; }

        public FlowElement CompensationTarget { get; }

        public ValueTask InvokeAsync(CompensationInvocationContext context, CancellationToken ct = default) {
            if (_failure is not null) throw _failure;

            _calls?.Add(CallId);
            context.Transitions.Add(new() {
                Process   = context.Process.Name,
                Token     = context.Scope.CanonicalName,
                Kind      = TransitionKind.Compensate,
                Previous  = Activity.Name,
                Posterior = CompensationTarget.Name,
                Event     = "Compensate",
            });
            return default;
        }
    }

    private sealed class RecordingObserver : ICompensationLifecycleObserver
    {
        public int Started { get; private set; }

        public int Completed { get; private set; }

        public Task OnCompensationStartedAsync(SchemataProcess process, TokenSnapshot scope, CancellationToken ct = default) {
            Started++;
            return Task.CompletedTask;
        }

        public Task OnCompensationCompletedAsync(SchemataProcess process, TokenSnapshot scope, CancellationToken ct = default) {
            Completed++;
            return Task.CompletedTask;
        }
    }
}
