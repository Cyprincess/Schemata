using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Flow.Bpmn.Runtime;
using Schemata.Flow.Bpmn.Runtime.Compensation;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Xunit;

namespace Schemata.Flow.Bpmn.Tests;

public class CompensationPrimitivesShould
{
    [Fact]
    public void Register_FivePushedInOrder_SnapshotReturnsInsertionOrder() {
        var handlers = NewHandlers();
        var stack    = new CompensationStack();

        foreach (var handler in handlers) stack.Register(handler);

        Assert.Equal(handlers, stack.Snapshot());
    }

    [Fact]
    public void Clear_AfterRegister_DropsAllHandlers() {
        var stack = new CompensationStack();

        foreach (var handler in NewHandlers()) stack.Register(handler);
        stack.Clear();

        Assert.Equal(0, stack.Count);
    }

    [Fact]
    public async Task InvokeAll_FivePushedAllSucceed_InvokesInReverseOrder() {
        var calls    = new List<string>();
        var handlers = NewHandlers(calls);
        var stack    = NewStack(handlers);

        var result = await CompensationCoordinator.InvokeAllAsync(stack, NewContext(), []);

        Assert.Equal(new[] { "E", "D", "C", "B", "A" }, calls);
        Assert.Equal(handlers.AsEnumerable().Reverse(), result.Compensated);
        Assert.Null(result.Failed);
        Assert.Null(result.FailureReason);
    }

    [Fact]
    public async Task InvokeAll_ThirdHandlerThrows_StopsAndReportsPartialCompletion() {
        var calls    = new List<string>();
        var failure  = new InvalidOperationException("boom");
        var handlers = NewHandlers(calls, "C", failure);
        var stack    = NewStack(handlers);

        var result = await CompensationCoordinator.InvokeAllAsync(stack, NewContext(), []);

        Assert.Equal(new[] { "E", "D" }, calls);
        Assert.Equal([handlers[4], handlers[3]], result.Compensated);
        Assert.Same(handlers[2], result.Failed);
        Assert.Same(failure, result.FailureReason);
    }

    [Fact]
    public async Task InvokeAll_OnSuccess_FiresStartedAndCompletedObservers() {
        var handlers = NewHandlers();
        var observer = new RecordingObserver();

        await CompensationCoordinator.InvokeAllAsync(NewStack(handlers), NewContext(), [observer]);

        Assert.Equal(5, observer.Started);
        Assert.Equal(1, observer.Completed);
    }

    [Fact]
    public async Task InvokeAll_OnFailure_FiresStartedButNotCompletedObserver() {
        var handlers = NewHandlers(failureId: "C", failure: new("boom"));
        var observer = new RecordingObserver();

        await CompensationCoordinator.InvokeAllAsync(NewStack(handlers), NewContext(), [observer]);

        Assert.Equal(3, observer.Started);
        Assert.Equal(0, observer.Completed);
    }

    [Fact]
    public async Task InvokeAll_ObserverThrows_DoesNotInterruptCoordinator() {
        var calls    = new List<string>();
        var handlers = NewHandlers(calls);

        var result = await CompensationCoordinator.InvokeAllAsync(
            NewStack(handlers),
            NewContext(),
            [new ThrowingObserver()]);

        Assert.Equal(new[] { "E", "D", "C", "B", "A" }, calls);
        Assert.Equal(5, result.Compensated.Count);
        Assert.Null(result.Failed);
    }

    private static CompensationInvocationContext NewContext() {
        return new(
            new() { DefinitionName = "process", CanonicalName = "processes/1" },
            new() { Name           = "process" },
            new() {
                CanonicalName = "processes/1/tokens/root",
                ScopeName       = "scope",
                StateName       = "scope",
                Status        = "Compensating",
            },
            new Dictionary<string, int> { ["scope"] = 1 });
    }

    private static CompensationStack NewStack(IEnumerable<ICompensationHandler> handlers) {
        var stack = new CompensationStack();

        foreach (var handler in handlers) stack.Register(handler);

        return stack;
    }

    private static List<FakeHandler> NewHandlers(
        List<string>?              calls     = null,
        string?                    failureId = null,
        InvalidOperationException? failure   = null) {
        return [
            new("A", calls, failureId == "A" ? failure : null),
            new("B", calls, failureId == "B" ? failure : null),
            new("C", calls, failureId == "C" ? failure : null),
            new("D", calls, failureId == "D" ? failure : null),
            new("E", calls, failureId == "E" ? failure : null),
        ];
    }

    private sealed class FakeHandler : ICompensationHandler
    {
        private readonly List<string>? _calls;
        private readonly Exception?    _failure;

        public FakeHandler(string id, List<string>? calls, Exception? failure) {
            Activity           = new NoneTask { Name = id };
            CompensationTarget = new NoneTask { Name = $"compensate-{id}" };
            _calls             = calls;
            _failure           = failure;
        }

        public Activity Activity { get; }

        public FlowElement CompensationTarget { get; }

        public ValueTask InvokeAsync(CompensationInvocationContext context, CancellationToken ct = default) {
            if (_failure is not null) throw _failure;

            _calls?.Add(Activity.Name);
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

    private sealed class ThrowingObserver : ICompensationLifecycleObserver
    {
        public Task OnCompensationStartedAsync(SchemataProcess process, TokenSnapshot scope, CancellationToken ct = default) {
            throw new InvalidOperationException("observer failed");
        }
    }
}
