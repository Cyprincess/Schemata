using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     Records its whole lifecycle into a <see cref="DisposalTracker" /> under a per-instance
///     index, so supervision tests can observe construction, start, turns, failure decisions,
///     stop and disposal of every instance a single spawn created. <see cref="Increment" />
///     replies with the serving instance's index, identifying which instance answered.
/// </summary>
public sealed class DisposalTrackingActor : IActor, IAsyncDisposable
{
    private readonly DisposalTracker _tracker;
    private readonly bool            _restartOnFailure;
    private readonly bool            _failSubsequentStarts;
    private readonly bool            _throwOnDispose;
    private readonly int             _index;

    public DisposalTrackingActor(DisposalTracker tracker, bool restartOnFailure, bool failSubsequentStarts = false, bool throwOnDispose = false) {
        _tracker              = tracker;
        _restartOnFailure     = restartOnFailure;
        _failSubsequentStarts = failSubsequentStarts;
        _throwOnDispose       = throwOnDispose;
        _index                = tracker.Register();
    }

    public ValueTask OnStartedAsync(IActorContext ctx) {
        if (_failSubsequentStarts && _index > 0) {
            throw new InvalidOperationException("startup failed");
        }

        _tracker.Record(_index, "OnStarted");
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        _tracker.Record(_index, "OnReceive");
        switch (envelope.Payload) {
            case Increment:
                await ctx.ReplyAsync(_index);
                break;
            case Fail fail:
                throw new InvalidOperationException(fail.Message);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) {
        _tracker.Record(_index, "OnStopped");
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) {
        _tracker.Record(_index, "OnFailed");
        return ValueTask.FromResult(_restartOnFailure);
    }

    public ValueTask DisposeAsync() {
        _tracker.Record(_index, "Disposed");
        if (_throwOnDispose) {
            throw new InvalidOperationException("dispose failed");
        }

        return ValueTask.CompletedTask;
    }
}
