using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class SelfAskShould
{
    [Fact]
    public async Task Reject_Direct_And_Logical_Self_Ask_Before_Enqueue_While_Allowing_Another_Actor() {
        var (system, _, root) = ActorSystemFactory.Create();
        try {
            var otherState = new ProbeState("other-reply");
            var selfState  = new ProbeState("self-reply");
            var other = await system.SpawnAsync(new("probe", "other"), new(typeof(Probe), [otherState]));
            var props = new Props(typeof(Probe), [selfState]);
            var self = await system.SpawnAsync(new("probe", "self"), props);
            var direct = await system.ResolveForSendAsync(root, self.Id, props, default);
            var stale = new MessageContext(new Dictionary<string, string?> { ["actor-turn"] = "stale" });

            Assert.Equal("rejected", await self.AskAsync<Call, string>(root, new(self, stale), timeout: TimeSpan.FromSeconds(30)));
            Assert.Equal("rejected", await self.AskAsync<Call, string>(root, new(direct, stale), timeout: TimeSpan.FromSeconds(30)));
            Assert.Equal("other-reply", await self.AskAsync<Call, string>(root, new(other, stale), timeout: TimeSpan.FromSeconds(30)));

            Assert.Equal(0, selfState.SelfEnqueueCount);
            Assert.Equal(1, otherState.SelfEnqueueCount);
            Assert.Equal(2, selfState.Errors.Count);
            Assert.All(selfState.Errors, error => Assert.Contains("cannot await an Ask to itself", error, StringComparison.Ordinal));
            Assert.Equal("self-reply", await self.AskAsync<Ping, string>(root, new(), stale, TimeSpan.FromSeconds(30)));
            Assert.Equal(1, selfState.SelfEnqueueCount);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    [Fact]
    public async Task Reject_Self_Ask_From_Start_And_Stop_Using_The_Current_Scope() {
        var (system, _, root) = ActorSystemFactory.Create();
        try {
            var otherState = new ProbeState("other-reply");
            var other = await system.SpawnAsync(new("lifecycle-probe", "other"), new(typeof(Probe), [otherState]));
            var state = new LifecycleProbeState { Other = other };
            var props = new Props(typeof(LifecycleProbe), [state]);
            var self = await system.SpawnAsync(new("lifecycle-probe", "self"), props);
            state.LogicalSelf = self;

            await state.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            state.DirectSelf = await system.ResolveForSendAsync(root, self.Id, props, default);
            state.ReleaseStart.TrySetResult();
            await state.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await system.StopAsync(self.Id);

            Assert.Equal(4, state.Errors.Count);
            Assert.All(state.Errors, error => Assert.Contains("cannot await an Ask to itself", error, StringComparison.Ordinal));
            Assert.Equal(["other-reply", "other-reply"], state.OtherReplies);
            Assert.Equal(0, state.SelfEnqueueCount);
            Assert.Equal(2, otherState.SelfEnqueueCount);
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    public sealed record Ping : IRequest<string>;
    public sealed record Call(IActorRef Target, MessageContext Context) : IRequest<string>;

    public sealed class ProbeState(string reply)
    {
        public string Reply { get; } = reply;
        public int SelfEnqueueCount { get; private set; }
        public List<string> Errors { get; } = [];

        public void Enqueued() {
            SelfEnqueueCount++;
        }
    }

    public sealed class Probe(ProbeState state) : IActor
    {
        public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;
        public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;
        public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception error) => ValueTask.FromResult(false);

        public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
            if (envelope.Payload is Ping) {
                state.Enqueued();
                await ctx.ReplyAsync(state.Reply);
                return;
            }

            var call = (Call)envelope.Payload;
            try {
                var response = await call.Target.AskAsync<Ping, string>(ctx.Services, new(), call.Context);
                await ctx.ReplyAsync(response);
            } catch (InvalidOperationException error) when (call.Target.Id == ctx.Self) {
                state.Errors.Add(error.Message);
                await ctx.ReplyAsync("rejected");
            }
        }
    }

    public sealed class LifecycleProbeState
    {
        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseStart { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IActorRef LogicalSelf { get; set; } = null!;
        public IActorRef DirectSelf { get; set; } = null!;
        public required IActorRef Other { get; init; }
        public MessageContext Stale { get; } = new(new Dictionary<string, string?> { ["actor-turn"] = "retained" });
        public List<string> Errors { get; } = [];
        public List<string> OtherReplies { get; } = [];
        public int SelfEnqueueCount { get; private set; }

        public void Enqueued() {
            SelfEnqueueCount++;
        }
    }

    public sealed class LifecycleProbe(LifecycleProbeState state) : IActor
    {
        public async ValueTask OnStartedAsync(IActorContext ctx) {
            state.StartEntered.TrySetResult();
            await state.ReleaseStart.Task;
            try {
                await RejectSelfAsk(ctx, state.DirectSelf);
                await RejectSelfAsk(ctx, state.LogicalSelf);
                state.OtherReplies.Add(await state.Other.AskAsync<Ping, string>(ctx.Services, new(), state.Stale));
                state.Started.TrySetResult();
            } catch (Exception error) {
                state.Started.TrySetException(error);
                throw;
            }
        }

        public async ValueTask OnStoppedAsync(IActorContext ctx) {
            await RejectSelfAsk(ctx, state.DirectSelf);
            await RejectSelfAsk(ctx, state.LogicalSelf);
            state.OtherReplies.Add(await state.Other.AskAsync<Ping, string>(ctx.Services, new(), state.Stale));
        }

        public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception error) => ValueTask.FromResult(false);

        public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
            state.Enqueued();
            return ctx.ReplyAsync("self-reply");
        }

        private async Task RejectSelfAsk(IActorContext ctx, IActorRef target) {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => target.AskAsync<Ping, string>(ctx.Services, new(), state.Stale, TimeSpan.FromSeconds(1)).AsTask());
            state.Errors.Add(error.Message);
        }
    }
}
