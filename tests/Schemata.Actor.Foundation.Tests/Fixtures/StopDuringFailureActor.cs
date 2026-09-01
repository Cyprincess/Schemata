using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     Counts its own real constructions and, on <see cref="Fail" />, blocks on a test-controlled
///     <see cref="ManualGate" /> before throwing - so a test can request a stop while the failing
///     turn is still in flight but before it actually throws, then verify whether supervision
///     (which would otherwise restart, since <see cref="OnFailedAsync" /> returns <see langword="true" />)
///     was consulted at all.
/// </summary>
public sealed class StopDuringFailureActor : IActor
{
    private readonly ManualGate _gate;

    public StopDuringFailureActor(ManualGate gate, SharedCounter constructionCount) {
        _gate = gate;
        constructionCount.Increment();
    }

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        if (envelope.Payload is Fail fail) {
            await _gate.WaitForReleaseAsync();
            throw new InvalidOperationException(fail.Message);
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}