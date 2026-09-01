using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;
using Schemata.Actor.Foundation.Runtime;
using Schemata.Actor.Foundation.Tests.Fixtures;
using Schemata.Actor.Skeleton;
using Xunit;

namespace Schemata.Actor.Foundation.Tests;

public class MailboxLoopShould
{
    [Fact]
    public async Task RunAsync_DrainsEveryItemUntilTheChannelActuallyCompletes_EvenWhenTheWriterCompletesWhileHandlingOne() {
        var channel   = Channel.CreateBounded<MailboxItem>(new BoundedChannelOptions(4) { SingleReader = true });
        var processed = new List<string>();

        // The first item's own handler both writes a second item and completes the channel
        // mid-turn - deterministically reproducing "a message arrives right as the writer
        // completes" without any timing race: the loop must still see and process that second
        // item, because RunAsync never terminates via cancellation, only via the channel itself
        // completing and draining.
        var loop = new MailboxLoop(channel.Reader, async item => {
            processed.Add(((Ping)item.Envelope.Payload).Text);
            if (processed.Count == 1) {
                await channel.Writer.WriteAsync(new MailboxItem(new Envelope(new Ping("second"))));
                channel.Writer.Complete();
            }
        });

        await channel.Writer.WriteAsync(new MailboxItem(new Envelope(new Ping("first"))));

        await loop.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["first", "second"], processed);
    }

    [Fact]
    public async Task RunAsync_SkipsAnItemCanceledWhileStillQueued_WithoutInvokingTheCallbackForIt() {
        var channel = Channel.CreateBounded<MailboxItem>(new BoundedChannelOptions(4) { SingleReader = true });
        var invoked = new List<string>();

        var canceled = new MailboxItem(new Envelope(new Ping("canceled")));
        Assert.True(canceled.TryCancel());

        await channel.Writer.WriteAsync(canceled);
        await channel.Writer.WriteAsync(new MailboxItem(new Envelope(new Ping("kept"))));
        channel.Writer.Complete();

        var loop = new MailboxLoop(channel.Reader, item => {
            invoked.Add(((Ping)item.Envelope.Payload).Text);
            return Task.CompletedTask;
        });

        await loop.RunAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(["kept"], invoked);
    }
}
