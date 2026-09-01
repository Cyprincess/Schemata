using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>Reports every stop notification it receives to a shared <see cref="StopNotifications" />, and fails (always stopping, never restarting) on <see cref="Fail" />.</summary>
public sealed class StopNotifyingActor(StopNotifications notifications) : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        if (envelope.Payload is Fail fail) {
            throw new InvalidOperationException(fail.Message);
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) {
        notifications.RecordStopped();
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(false);
}