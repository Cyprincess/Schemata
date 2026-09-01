using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Internal;

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

/// <summary>
///     A mailbox entry paired with an atomic <c>Queued -&gt; Executing</c> / <c>Queued -&gt;
///     Canceled</c> state bit, so a caller who gave up waiting on a still-queued <c>Ask</c> can
///     mark it canceled instead of the message being run for no listener.
/// </summary>
internal sealed class MailboxItem(Envelope envelope) : IDisposable
{
    private const int Queued    = 0;
    private const int Executing = 1;
    private const int Canceled  = 2;

    private readonly CancellationTokenSource _cancellation = new();

    private int _state;

    public Envelope Envelope { get; } = envelope;

    /// <summary>
    ///     Signaled when the caller waiting on this item's <c>Ask</c> gives up (timeout or its own
    ///     cancellation) after this item has already started executing - observable by a
    ///     still-running handler through the turn's <see cref="IActorContext.Stopping" />.
    /// </summary>
    public CancellationToken Cancellation => _cancellation.Token;

    /// <summary>Transitions this item from <c>Queued</c> to <c>Executing</c>.</summary>
    /// <returns><see langword="false" /> when it was already canceled while queued.</returns>
    public bool TryBeginExecuting() => Interlocked.CompareExchange(ref _state, Executing, Queued) == Queued;

    /// <summary>Cancels this item while it is still queued.</summary>
    /// <returns><see langword="false" /> once execution has already begun.</returns>
    public bool TryCancel() => Interlocked.CompareExchange(ref _state, Canceled, Queued) == Queued;

    /// <summary>Signals a still-executing turn that the caller waiting on this item has given up.</summary>
    public void CancelDelivery() {
        try {
            _cancellation.Cancel();
        } catch (ObjectDisposedException) {
            // The turn already finished (and this item was already disposed) between the caller
            // losing the TryCancel race and calling this - nothing left to signal.
        }
    }

    public void Dispose() => _cancellation.Dispose();
}
