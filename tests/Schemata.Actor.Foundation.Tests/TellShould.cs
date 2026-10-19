using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class TellShould
{
    [Fact]
    public async Task Tell_DeliversTheMessageToTheActor() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new("recorder", "a"), new(typeof(TellRecordingActor)));

        await actor.TellAsync(new RecordTell("hello"));

        var received = await actor.AskAsync<GetReceived, IReadOnlyList<string>>(new());
        Assert.Equal(["hello"], received);
    }

    [Fact]
    public async Task Tell_ReturnsWithoutWaitingForTheHandlerToFinish() {
        var (system, _, _) = ActorSystemFactory.Create();
        var gate           = new ManualGate();
        var actor          = await system.SpawnAsync(new("gated", "a"), new(typeof(GatedActor), [gate]));

        await actor.TellAsync(new GateAndWait());

        // The handler reaches the gate only by actually executing the turn, so this handshake
        // proves TellAsync already returned while the handler is still in flight.
        await gate.Started.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(gate.TurnCompleted.IsCompleted);

        gate.Release();
        await gate.TurnCompleted.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Tell_LeavesNoPendingReplyTableResidue_SoASubsequentAskStillResolves() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new("recorder", "b"), new(typeof(TellRecordingActor)));

        for (var i = 0; i < 50; i++) {
            await actor.TellAsync(new RecordTell($"m{i}"));
        }

        var received = await actor.AskAsync<GetReceived, IReadOnlyList<string>>(new());
        Assert.Equal(50, received.Count);
    }
}
