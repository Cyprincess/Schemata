using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class MailboxFifoShould
{
    [Fact]
    public async Task MailboxFifo_ProcessesConcurrentlySentMessages_InTheOrderTheyWereSent() {
        var (system, _, _) = ActorSystemFactory.Create();
        var actor           = await system.SpawnAsync(new ActorId("order", "a"), new Props(typeof(OrderRecordingActor)));

        // Fire all 30 Asks without awaiting each individually first, so they race to enqueue -
        // FIFO ordering must come from the single-consumer mailbox, not from the caller awaiting
        // one at a time.
        var pending = Enumerable.Range(0, 30)
                                 .Select(i => actor.AskAsync<Sequenced, int>(new Sequenced(i)).AsTask())
                                 .ToArray();
        await Task.WhenAll(pending);

        var order = await actor.AskAsync<GetOrder, IReadOnlyList<int>>(new GetOrder());
        Assert.Equal(Enumerable.Range(0, 30), order);
    }
}
