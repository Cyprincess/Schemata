using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Schemata.Entity.Repository;
using Schemata.Flow.Skeleton.Builders;
using Schemata.Flow.Skeleton.Entities;
using Schemata.Flow.Skeleton.Models;
using Schemata.Flow.Skeleton.Runtime;
using Schemata.Flow.StateMachine;
using Xunit;

namespace Schemata.Flow.Tests;

public class FlowEffectRecordingShould
{
    [Fact]
    public async Task Forward_Stable_Request_Id_To_Task_Body() {
        string?  observed   = null;
        var      definition = new EffectProcess(context => { observed = context.RequestId; return ValueTask.CompletedTask; });
        var      engine     = new StateMachineEngine();
        var      process    = NewProcess();

        var started = await engine.StartAsync(definition, process, Context());

        Assert.NotNull(observed);
        Assert.StartsWith($"processes/p1/tokens/", observed);
        Assert.EndsWith("/effects/0", observed);
        Assert.Equal(definition.Work.Name, started.Tokens[0].StateName);
    }

    [Fact]
    public async Task Derive_Distinct_Request_Ids_For_Successive_Effects() {
        var ids        = new List<string?>();
        var definition = new TwoEffectProcess(
            context => ids.Add(context.RequestId),
            context => ids.Add(context.RequestId));
        var engine  = new StateMachineEngine();
        var process = NewProcess();
        var context = Context();

        var started  = await engine.StartAsync(definition, process, context);
        var advanced = await engine.AdvanceAsync(definition, process, started.Tokens, context);

        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.NotNull(id));
        Assert.Equal(2, ids.Distinct().Count());
        Assert.EndsWith("/effects/0", ids[0]);
        Assert.EndsWith("/effects/1", ids[1]);
        Assert.Equal(definition.Second.Name, advanced.Tokens[0].StateName);
    }

    [Fact]
    public async Task Record_Intent_And_Completion_Around_Body() {
        var calls   = new List<string>();
        var intents = new List<FlowEffectIntent>();
        var recorder = Recorder(intents, calls);
        var definition = new EffectProcess(context => {
            calls.Add($"body:{context.RequestId}");
            return ValueTask.CompletedTask;
        });

        var engine  = new StateMachineEngine();
        var process = NewProcess();

        await engine.StartAsync(definition, process, Context(recorder));

        var intent = Assert.Single(intents);
        Assert.Equal($"intent:{intent.RequestId}", calls[0]);
        Assert.Equal($"body:{intent.RequestId}", calls[1]);
        Assert.Equal($"completion:{intent.RequestId}", calls[2]);
        Assert.Equal("processes/p1", intent.Process);
        Assert.Equal("Enter_Work", intent.Task);
        Assert.StartsWith("processes/p1/tokens/", intent.Token);
        Assert.Equal(FlowEffectState.Recorded, intent.State);
    }

    [Fact]
    public async Task Skip_Body_When_Intent_Already_Completed() {
        var intents = new List<FlowEffectIntent>();
        var recorder = new Mock<IFlowEffectRecorder>();
        recorder.Setup(r => r.CountIntentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);
        recorder.Setup(r => r.RecordIntentAsync(It.IsAny<FlowEffectIntent>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FlowEffectIntent intent, IUnitOfWork _, CancellationToken _) => {
                    intents.Add(intent);
                    return new FlowEffectIntent {
                        RequestId = intent.RequestId,
                        Process   = intent.Process,
                        Token     = intent.Token,
                        Task      = intent.Task,
                        State     = FlowEffectState.Completed,
                    };
                });

        var invoked    = false;
        var definition = new EffectProcess(_ => { invoked = true; return ValueTask.CompletedTask; });
        var engine     = new StateMachineEngine();
        var process    = NewProcess();

        var started = await engine.StartAsync(definition, process, Context(recorder));

        Assert.False(invoked);
        Assert.Equal(definition.Work.Name, started.Tokens[0].StateName);
        recorder.Verify(
            r => r.RecordCompletionAsync(It.IsAny<string>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Resurface_Outcome_Unknown_When_Intent_Recorded_Unknown() {
        var recorder = new Mock<IFlowEffectRecorder>();
        recorder.Setup(r => r.CountIntentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(1L);
        recorder.Setup(r => r.RecordIntentAsync(It.IsAny<FlowEffectIntent>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FlowEffectIntent intent, IUnitOfWork _, CancellationToken _) => new FlowEffectIntent {
                    RequestId = intent.RequestId,
                    Process   = intent.Process,
                    Token     = intent.Token,
                    Task      = intent.Task,
                    State     = FlowEffectState.OutcomeUnknown,
                });

        var invoked    = false;
        var definition = new EffectProcess(_ => { invoked = true; return ValueTask.CompletedTask; });
        var engine     = new StateMachineEngine();

        await Assert.ThrowsAsync<FlowEffectOutcomeUnknownException>(
            () => engine.StartAsync(definition, NewProcess(), Context(recorder)).AsTask());

        Assert.False(invoked);
    }

    [Fact]
    public async Task Surface_Outcome_Unknown_Report_Through_Process_Failure_Path() {
        var calls   = new List<string>();
        var intents = new List<FlowEffectIntent>();
        var recorder = Recorder(intents, calls);
        var definition = new EffectProcess(context => {
            calls.Add($"body:{context.RequestId}");
            context.ReportOutcomeUnknown();
            return ValueTask.CompletedTask;
        });

        var engine = new StateMachineEngine();

        var exception = await Assert.ThrowsAsync<FlowEffectOutcomeUnknownException>(
            () => engine.StartAsync(definition, NewProcess(), Context(recorder)).AsTask());

        var intent = Assert.Single(intents);
        Assert.Equal($"intent:{intent.RequestId}", calls[0]);
        Assert.Equal($"body:{intent.RequestId}", calls[1]);
        Assert.Equal($"unknown:{intent.RequestId}", calls[2]);
        recorder.Verify(
            r => r.RecordCompletionAsync(It.IsAny<string>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Equal(Schemata.Abstractions.SchemataConstants.ErrorReasons.FlowEffectOutcomeUnknown, exception.Details?.OfType<Schemata.Abstractions.Errors.ErrorInfoDetail>().FirstOrDefault()?.Reason);
    }

    [Fact]
    public async Task Not_Record_Completion_When_Body_Throws() {
        var calls   = new List<string>();
        var intents = new List<FlowEffectIntent>();
        var recorder = Recorder(intents, calls);
        var definition = new EffectProcess(_ => throw new InvalidOperationException("body boom"));

        var engine = new StateMachineEngine();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => engine.StartAsync(definition, NewProcess(), Context(recorder)).AsTask());

        Assert.Single(intents);
        Assert.DoesNotContain(calls, c => c.StartsWith("completion:", StringComparison.Ordinal));
        Assert.DoesNotContain(calls, c => c.StartsWith("unknown:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Run_Without_Recorder_Still_Forwards_Request_Id() {
        string?  observed   = null;
        var      definition = new EffectProcess(context => { observed = context.RequestId; return ValueTask.CompletedTask; });
        var      engine     = new StateMachineEngine();

        var started = await engine.StartAsync(definition, NewProcess(), Context());

        Assert.NotNull(observed);
        Assert.Equal("Active", started.Tokens[0].State);
    }

    private static Mock<IFlowEffectRecorder> Recorder(List<FlowEffectIntent> intents, List<string> calls) {
        var recorder = new Mock<IFlowEffectRecorder>();
        recorder.Setup(r => r.CountIntentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(0L);
        recorder.Setup(r => r.RecordIntentAsync(It.IsAny<FlowEffectIntent>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((FlowEffectIntent intent, IUnitOfWork _, CancellationToken _) => {
                    calls.Add($"intent:{intent.RequestId}");
                    intents.Add(intent);
                    return intent;
                });
        recorder.Setup(r => r.RecordCompletionAsync(It.IsAny<string>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()))
                .Callback((string id, IUnitOfWork _, CancellationToken _) => calls.Add($"completion:{id}"))
                .Returns(ValueTask.CompletedTask);
        recorder.Setup(r => r.RecordOutcomeUnknownAsync(It.IsAny<string>(), It.IsAny<IUnitOfWork>(), It.IsAny<CancellationToken>()))
                .Callback((string id, IUnitOfWork _, CancellationToken _) => calls.Add($"unknown:{id}"))
                .Returns(ValueTask.CompletedTask);
        return recorder;
    }

    private static FlowExecutionContext Context(Mock<IFlowEffectRecorder>? recorder = null) {
        var services = new ServiceCollection();
        if (recorder is not null) {
            services.AddSingleton(recorder.Object);
        }

        return FlowTestCreation.Context(Mock.Of<IUnitOfWork>(), services.BuildServiceProvider());
    }

    private static SchemataProcess NewProcess() {
        return new() { Name = "p1", CanonicalName = "processes/p1", DefinitionName = "effect-process" };
    }

    private sealed class EffectProcess : ProcessDefinition
    {
        public EffectProcess(Func<FlowTaskContext, ValueTask> body) {
            this.Start().Go(Work);
            this.During(Work).OnEnter((context, _) => body(context)).End();
        }

        public NoneTask Work { get; } = null!;
    }

    private sealed class TwoEffectProcess : ProcessDefinition
    {
        public TwoEffectProcess(Action<FlowTaskContext> first, Action<FlowTaskContext> second) {
            this.Start().Go(First);
            this.During(First).OnEnter((context, _) => { first(context); return ValueTask.CompletedTask; }).Go(Second);
            this.During(Second).OnEnter((context, _) => { second(context); return ValueTask.CompletedTask; }).End();
        }

        public NoneTask First  { get; } = null!;
        public NoneTask Second { get; } = null!;
    }
}
