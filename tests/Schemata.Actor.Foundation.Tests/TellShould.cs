using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class TellShould
{
    [Fact]
    public async Task Tell_DeliversTheMessageToTheActor() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("recorder", "a"), new Props(typeof(TellRecordingActor)));

        await actor.TellAsync(new RecordTell("hello"));

        var received = await actor.AskAsync<GetReceived, IReadOnlyList<string>>(new GetReceived());
        Assert.Equal(["hello"], received);
    }

    [Fact]
    public async Task Tell_ReturnsWithoutWaitingForTheHandlerToFinish() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("versatile", "a"), new Props(typeof(VersatileActor)));

        var sw = Stopwatch.StartNew();
        await actor.TellAsync(new SlowPing(TimeSpan.FromSeconds(5)));
        sw.Stop();

        // The mailbox write completes immediately; it never waits for the slow handler turn it
        // just enqueued (which sleeps for five seconds).
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Tell_LeavesNoPendingReplyTableResidue_SoASubsequentAskStillResolves() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("recorder", "b"), new Props(typeof(TellRecordingActor)));

        for (var i = 0; i < 50; i++) {
            await actor.TellAsync(new RecordTell($"m{i}"));
        }

        var received = await actor.AskAsync<GetReceived, IReadOnlyList<string>>(new GetReceived());
        Assert.Equal(50, received.Count);
    }
}
