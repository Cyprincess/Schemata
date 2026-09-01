using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     An <see cref="IPersistentActor" /> holding an in-memory counter, serialized to/from
///     <see cref="IPersistentActor.SaveStateAsync" />/<see cref="IPersistentActor.LoadStateAsync" />
///     as its four raw bytes. <see cref="Increment" /> mutates and replies with the new count;
///     <see cref="GetCount" /> replies with the current count without mutating it.
/// </summary>
public sealed class CounterPersistentActor : IPersistentActor
{
    private int _count;

    #region IPersistentActor Members

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case Increment:
                _count++;
                await ctx.ReplyAsync(_count);
                break;
            case GetCount:
                await ctx.ReplyAsync(_count);
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);

    public ValueTask<byte[]?> SaveStateAsync(IActorContext ctx) => ValueTask.FromResult<byte[]?>(BitConverter.GetBytes(_count));

    public ValueTask LoadStateAsync(IActorContext ctx, byte[] state, CancellationToken ct) {
        _count = BitConverter.ToInt32(state, 0);

        return ValueTask.CompletedTask;
    }

    #endregion
}