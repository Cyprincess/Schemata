using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     A stateful counter whose supervision disposition on failure is fixed at construction, so a
///     test can register the same behavior under either a restart or a stop policy via
///     <see cref="Props.Args" />.
/// </summary>
public sealed class SupervisedActor(bool restartOnFailure) : IActor
{
    public Guid InstanceId { get; } = Guid.NewGuid();

    private int _count;

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case Increment:
                _count++;
                await ctx.ReplyAsync(_count);
                break;
            case WhoAmI:
                await ctx.ReplyAsync(InstanceId);
                break;
            case Fail fail:
                throw new InvalidOperationException(fail.Message);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(restartOnFailure);
}