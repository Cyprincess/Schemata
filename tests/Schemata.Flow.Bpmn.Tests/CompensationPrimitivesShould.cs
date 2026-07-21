using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
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
        var started = 0;
        var completed = 0;
        var observer = new Mock<ICompensationLifecycleObserver>();
        observer.Setup(o => o.OnCompensationStartedAsync(It.IsAny<SchemataProcess>(), It.IsAny<TokenSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback(() => started++)
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnCompensationCompletedAsync(It.IsAny<SchemataProcess>(), It.IsAny<TokenSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback(() => completed++)
                .Returns(Task.CompletedTask);

        await CompensationCoordinator.InvokeAllAsync(NewStack(handlers), NewContext(), [observer.Object]);

        Assert.Equal(5, started);
        Assert.Equal(1, completed);
    }

    [Fact]
    public async Task InvokeAll_OnFailure_FiresStartedButNotCompletedObserver() {
        var handlers = NewHandlers(failureId: "C", failure: new("boom"));
        var started = 0;
        var completed = 0;
        var observer = new Mock<ICompensationLifecycleObserver>();
        observer.Setup(o => o.OnCompensationStartedAsync(It.IsAny<SchemataProcess>(), It.IsAny<TokenSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback(() => started++)
                .Returns(Task.CompletedTask);
        observer.Setup(o => o.OnCompensationCompletedAsync(It.IsAny<SchemataProcess>(), It.IsAny<TokenSnapshot>(), It.IsAny<CancellationToken>()))
                .Callback(() => completed++)
                .Returns(Task.CompletedTask);

        await CompensationCoordinator.InvokeAllAsync(NewStack(handlers), NewContext(), [observer.Object]);

        Assert.Equal(3, started);
        Assert.Equal(0, completed);
    }

    [Fact]
    public async Task InvokeAll_ObserverThrows_DoesNotInterruptCoordinator() {
        var calls    = new List<string>();
        var handlers = NewHandlers(calls);
        var observer = new Mock<ICompensationLifecycleObserver>();
        observer.Setup(o => o.OnCompensationStartedAsync(It.IsAny<SchemataProcess>(), It.IsAny<TokenSnapshot>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("observer failed"));

        var result = await CompensationCoordinator.InvokeAllAsync(
            NewStack(handlers),
            NewContext(),
            [observer.Object]);

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

    private static List<ICompensationHandler> NewHandlers(
        List<string>?              calls     = null,
        string?                    failureId = null,
        InvalidOperationException? failure   = null) {
        return [
            CreateHandler("A", calls, failureId == "A" ? failure : null).Object,
            CreateHandler("B", calls, failureId == "B" ? failure : null).Object,
            CreateHandler("C", calls, failureId == "C" ? failure : null).Object,
            CreateHandler("D", calls, failureId == "D" ? failure : null).Object,
            CreateHandler("E", calls, failureId == "E" ? failure : null).Object,
        ];
    }

    private static Mock<ICompensationHandler> CreateHandler(
        string          id,
        List<string>?   calls,
        Exception?      failure
    ) {
        var handler = new Mock<ICompensationHandler>(MockBehavior.Strict);
        var activity = new NoneTask { Name = id };
        handler.SetupGet(value => value.Activity).Returns(activity);
        handler.SetupGet(value => value.CompensationTarget).Returns(new NoneTask { Name = $"compensate-{id}" });
        var invocation = handler.Setup(value => value.InvokeAsync(
            It.IsAny<CompensationInvocationContext>(),
            It.IsAny<CancellationToken>()));
        if (failure is not null) {
            invocation.Throws(failure);
        } else {
            invocation.Callback(() => calls?.Add(id)).Returns(ValueTask.CompletedTask);
        }

        return handler;
    }

}
