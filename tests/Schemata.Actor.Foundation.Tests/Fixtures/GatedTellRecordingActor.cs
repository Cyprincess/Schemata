using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

/// <summary>
///     Blocks its turn on a test-controlled <see cref="ManualGate" /> for <see cref="GateAndWait" />
///     and records every <see cref="RecordTell" /> into a caller-owned list, so observations survive
///     activation boundaries (the registry reuses the same props arguments per activation).
/// </summary>
public sealed class GatedTellRecordingActor(ManualGate gate, List<string> received) : IActor
{
    public ValueTask OnStartedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public async ValueTask OnReceiveAsync(IActorContext ctx, Envelope envelope) {
        switch (envelope.Payload) {
            case GateAndWait:
                await gate.WaitForReleaseAsync();
                gate.NotifyTurnCompleted();
                await ctx.ReplyAsync("released");
                break;
            case RecordTell record:
                received.Add(record.Value);
                break;
        }
    }

    public ValueTask OnStoppedAsync(IActorContext ctx) => ValueTask.CompletedTask;

    public ValueTask<bool> OnFailedAsync(IActorContext ctx, Exception ex) => ValueTask.FromResult(true);
}
