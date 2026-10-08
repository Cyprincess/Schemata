using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;
using Schemata.Messaging.Skeleton.Runtime;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public sealed class LogicalLifetimeShould
{
    [Fact]
    public async Task Retained_Reference_Reactivates_After_Idle_And_Pending_Turn_Prevents_Collection() {
        var clock = new Mock<TimeProvider>();
        long now = 0;
        clock.Setup(value => value.GetTimestamp()).Returns(() => now);
        clock.SetupGet(value => value.TimestampFrequency).Returns(1);
        using var root = new ServiceCollection().BuildServiceProvider();
        var registry = new ActorRegistry();
        var state = new State();
        var props = new Props(typeof(ProbeActor), [state]);
        var system = new InProcessActorSystem(root, registry, new MessageExecutionScopeFactory(root.GetRequiredService<IServiceScopeFactory>()), Options.Create(new SchemataActorOptions()), clock.Object);
        var reference = await system.SpawnAsync(new("probe", "idle"), props);
        var first = await reference.AskAsync<Identity, Guid>(new());
        var activation = await system.ResolveForSendAsync(reference.Id, props, default);
        var blocked = reference.AskAsync<Block, Guid>(new()).AsTask();
        await state.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        now = 1000;
        Assert.False(activation.TryRetireIdle(TimeSpan.FromMinutes(15)));
        state.Release.SetResult();
        Assert.Equal(first, await blocked);
        await reference.AskAsync<Identity, Guid>(new());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true) {
            now += 1000;
            if (activation.TryRetireIdle(TimeSpan.FromMinutes(15))) break;
            deadline.Token.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        await activation.Completion;
        Assert.NotEqual(first, await reference.AskAsync<Identity, Guid>(new()));
        await system.StopAsync(reference.Id);
    }

    [Fact]
    public async Task Replace_And_Cancel_Queued_Timer_Ticks_Before_Callback_Execution() {
        var callbacks = new List<(TimerCallback Callback, object? State)>();
        var clock = new Mock<TimeProvider>();
        clock.Setup(value => value.CreateTimer(It.IsAny<TimerCallback>(), It.IsAny<object?>(), It.IsAny<TimeSpan>(), It.IsAny<TimeSpan>()))
            .Returns<TimerCallback, object?, TimeSpan, TimeSpan>((callback, state, _, _) => {
                callbacks.Add((callback, state));
                return Mock.Of<ITimer>();
            });
        using var root = new ServiceCollection().BuildServiceProvider();
        var state = new State();
        var props = new Props(typeof(ProbeActor), [state]);
        var system = new InProcessActorSystem(root, new ActorRegistry(), new MessageExecutionScopeFactory(root.GetRequiredService<IServiceScopeFactory>()), Options.Create(new SchemataActorOptions()), clock.Object);
        var reference = await system.SpawnAsync(new("probe", "timers"), props);
        await reference.AskAsync<Identity, Guid>(new());
        var activation = await system.ResolveForSendAsync(reference.Id, props, default);
        var blocked = reference.AskAsync<Block, Guid>(new()).AsTask();
        await state.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var old = 0;
        var current = 0;
        activation.RegisterTimer("tick", _ => { old++; return ValueTask.CompletedTask; }, TimeSpan.Zero, null);
        callbacks[0].Callback(callbacks[0].State);
        activation.RegisterTimer("tick", _ => { current++; return ValueTask.CompletedTask; }, TimeSpan.Zero, null);
        callbacks[1].Callback(callbacks[1].State);
        state.Release.SetResult();
        await blocked;
        await reference.AskAsync<Identity, Guid>(new());
        Assert.Equal(0, old);
        Assert.Equal(1, current);
        activation.RegisterTimer("cancel", _ => { current++; return ValueTask.CompletedTask; }, TimeSpan.Zero, null);
        activation.CancelTimer("cancel");
        activation.CancelTimer("cancel");
        callbacks[2].Callback(callbacks[2].State);
        await reference.AskAsync<Identity, Guid>(new());
        Assert.Equal(1, current);
        await system.StopAsync(reference.Id);
        callbacks[1].Callback(callbacks[1].State);
        Assert.Equal(1, current);
    }

    public sealed record Identity : IRequest<Guid>;
    public sealed record Block : IRequest<Guid>;
    public sealed class State {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class ProbeActor(State state) : IActor {
        private readonly Guid _id = Guid.NewGuid();
        public ValueTask OnStartedAsync(IActorContext context) => ValueTask.CompletedTask;
        public async ValueTask OnReceiveAsync(IActorContext context, Envelope envelope) {
            if (envelope.Payload is Block) { state.Entered.TrySetResult(); await state.Release.Task; }
            await context.ReplyAsync(_id);
        }
        public ValueTask<bool> OnFailedAsync(IActorContext context, Exception error) => new(false);
        public ValueTask OnStoppedAsync(IActorContext context) => ValueTask.CompletedTask;
    }
}
