using System;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Scheduling.Tests.Fixtures;

/// <summary>Remembers the last <see cref="ReminderPayload" /> it received, answering <see cref="GetReceived" /> with it.</summary>
public sealed class RecordingActor : IActor
{
    private ReminderPayload? _received;

    #region IActor Members

    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case GetReceived:
                await ctx.ReplyAsync<ReminderPayload?>(_received);
                break;
            case ReminderPayload payload:
                _received = payload;
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);

    #endregion
}