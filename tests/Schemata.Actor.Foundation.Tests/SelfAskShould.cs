using System;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class SelfAskShould
{
    [Fact]
    public async Task Reject_Self_Ask_Before_Enqueue_While_Allowing_Another_Actor() {
        var (system, _, root) = ActorSystemFactory.Create();
        try {
            var other = await system.SpawnAsync(new("probe", "other"), new(typeof(Probe)));
            var self = await system.SpawnAsync(new("probe", "self"), new(typeof(Probe)));
            var rejected = await self.AskAsync<Call, string>(new(self), timeout: TimeSpan.FromSeconds(5));
            Assert.Equal("rejected", rejected);
            Assert.Equal("pong", await self.AskAsync<Call, string>(new(other), timeout: TimeSpan.FromSeconds(5)));
            Assert.Equal("pong", await self.AskAsync<Ping, string>(new(), timeout: TimeSpan.FromSeconds(5)));
        } finally {
            await system.ShutdownAsync(default);
            if (root is IAsyncDisposable disposable) await disposable.DisposeAsync();
        }
    }

    public sealed record Ping : IRequest<string>;
    public sealed record Call(IActorRef Target) : IRequest<string>;
    public sealed class Probe : IActor
    {
        public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;
        public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;
        public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception error) => ValueTask.FromResult(false);
        public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
            if (envelope.Payload is Ping) { await ctx.ReplyAsync("pong"); return; }
            var call = (Call)envelope.Payload;
            try {
                var response = await call.Target.AskAsync<Ping, string>(new());
                await ctx.ReplyAsync(response);
            } catch (InvalidOperationException) when (call.Target.Id == ctx.Self) {
                await ctx.ReplyAsync("rejected");
            }
        }
    }
}
