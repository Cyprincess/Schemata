using System;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Schemata.Actor.Foundation.Runtime;

/// <summary>
///     Drains a bounded mailbox channel one item at a time, waiting for each turn to fully
///     complete - including releasing its own scope - before reading the next.
/// </summary>
/// <remarks>
///     Purely mechanical: it owns the drain loop, the queued-cancel skip check, and disposing
///     every item it dequeues, nothing about what a turn does.
///     <see cref="ActorInstance" /> supplies <paramref name="processItem" /> and owns every
///     actor-specific concern (scope, ambient context, supervision, the Ask protocol).
/// </remarks>
/// <remarks>
///     Termination is driven purely by the channel completing and draining - never by
///     cancellation. A stop must still deliver an "actor stopped" fault to every item already
///     accepted into the channel before the stop, and only <see cref="ChannelWriter{T}.Complete" />
///     (never cancellation) guarantees that: canceling this loop's own read-wait could abandon
///     items that are sitting in the channel but have not been read yet, leaving their callers
///     hanging forever.
/// </remarks>
internal sealed class MailboxLoop(ChannelReader<MailboxItem> reader, Func<MailboxItem, Task> processItem)
{
    public async Task RunAsync() {
        while (await reader.WaitToReadAsync()) {
            while (reader.TryRead(out var item)) {
                try {
                    if (item.TryBeginExecuting()) {
                        await processItem(item);
                    }

                    // A caller that gave up while this item was still queued already CAS'd it to
                    // Canceled; ChannelWriter.WriteAsync's own cancellation cannot pull a message
                    // back out of the channel once written, so this CAS is what makes "canceled
                    // while queued" observable and skips the item instead of running its handler
                    // for a listener that is no longer there.
                } finally {
                    item.Dispose();
                }
            }
        }
    }
}