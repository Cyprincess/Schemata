using System;
using System.Threading;
using Schemata.Actor.Skeleton;

namespace Schemata.Actor.Foundation.Runtime;

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