using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Schemata.Actor.Skeleton.Entities;
using Schemata.Messaging.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class TurnScopeShould
{
    [Fact]
    public async Task TurnScope_AcrossThreeTurns_ResolvesADifferentScopedInstanceEachTime_AndDisposesEachOnceItsTurnEnds() {
        var (system, _, root) = ActorSystemFactory.Create(services => {
            services.AddSingleton<MarkerRegistry>();
            services.AddScoped<ScopedMarker>();
        });
        var actor = await system.SpawnAsync(new("scope-probe", "a"), new(typeof(ScopeProbeActor)));

        var id1 = await actor.AskAsync<GetMarkerId, Guid>(root, new());
        var id2 = await actor.AskAsync<GetMarkerId, Guid>(root, new());
        // A third turn's completion guarantees the second turn's own scope has already been
        // released - the single-consumer mailbox loop cannot start turn N+1 until turn N's whole
        // "await using" scope block, disposal included, has returned.
        await actor.AskAsync<GetMarkerId, Guid>(root, new());

        Assert.NotEqual(id1, id2);

        var registry = root.GetRequiredService<MarkerRegistry>();
        var marker1  = registry.Instances.Single(m => m.Id == id1);
        var marker2  = registry.Instances.Single(m => m.Id == id2);

        Assert.True(marker1.Disposed);
        Assert.True(marker2.Disposed);
    }

    [Fact]
    public async Task MessageRunner_Leaves_Actor_Turn_Identity_Unbound() {
        var (system, _, root) = ActorSystemFactory.Create();
        var runner = root.GetRequiredService<IMessageExecutionScopeRunner>();
        var target = new ActorId("scope-probe", "ordinary");

        var current = await runner.RunAsync(null,
            (services, _) => ValueTask.FromResult(services.GetRequiredService<ActorTurnIdentity>().IsCurrent(target, system)));

        Assert.False(current);
        await system.ShutdownAsync(default);
    }

    [Fact]
    public async Task Failure_Leaves_The_Next_Scope_With_One_Fresh_Binding() {
        var (system, _, root) = ActorSystemFactory.Create();
        var actor = await system.SpawnAsync(new("identity-probe", "failure"), new(typeof(IdentityProbeActor), [system]));

        Assert.True(await actor.AskAsync<InspectIdentity, bool>(root, new()));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => actor.AskAsync<FailTurn, bool>(root, new()).AsTask());
        Assert.Equal("turn failed", error.Message);
        Assert.True(await actor.AskAsync<InspectIdentity, bool>(root, new()));

        await system.ShutdownAsync(default);
    }

    [Fact]
    public async Task Cancellation_Leaves_The_Next_Scope_With_One_Fresh_Binding() {
        var (system, _, root) = ActorSystemFactory.Create();
        var actor = await system.SpawnAsync(new("identity-probe", "cancellation"), new(typeof(IdentityProbeActor), [system]));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = new CancellationTokenSource();
        var pending = actor.AskAsync<BlockTurn, bool>(root, new(entered), ct: cancellation.Token).AsTask();

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(await actor.AskAsync<InspectIdentity, bool>(root, new(), timeout: TimeSpan.FromSeconds(30)));

        await system.ShutdownAsync(default);
    }

    public sealed record InspectIdentity : IRequest<bool>;
    public sealed record FailTurn : IRequest<bool>;
    public sealed record BlockTurn(TaskCompletionSource Entered) : IRequest<bool>;

    public sealed class IdentityProbeActor(InProcessActorSystem<SchemataActor> system) : IActor
    {
        public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;
        public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;
        public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception error) => ValueTask.FromResult(true);

        public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
            switch (envelope.Payload) {
                case InspectIdentity:
                    await ctx.ReplyAsync(HasSingleBinding(ctx));
                    break;
                case FailTurn:
                    Assert.True(HasSingleBinding(ctx));
                    throw new InvalidOperationException("turn failed");
                case BlockTurn block:
                    Assert.True(HasSingleBinding(ctx));
                    block.Entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, ctx.Stopping);
                    await ctx.ReplyAsync(true);
                    break;
            }
        }

        private bool HasSingleBinding(IActorContext ctx) {
            var identity = ctx.Services.GetRequiredService<ActorTurnIdentity>();
            Assert.True(identity.IsCurrent(ctx.Self, system));
            var error = Assert.Throws<InvalidOperationException>(() => identity.Bind(ctx.Self, system));
            Assert.Contains("already bound", error.Message, StringComparison.Ordinal);
            return true;
        }
    }
}
