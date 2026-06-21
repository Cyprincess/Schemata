using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Push.Skeleton;

/// <summary>
///     A delivery backend (e.g. FCM, SignalR, SMTP). Each transport inspects the
///     <see cref="PushContext.Target" /> and its own subscription state to decide whether it
///     handles a dispatch, reporting <see cref="TransportStatus.Skipped" /> when it does not.
/// </summary>
public interface IPushTransport
{
    /// <summary>The stable transport name (e.g. <c>fcm</c>, <c>signalr</c>).</summary>
    string Name { get; }

    /// <summary>
    ///     Attempts delivery for one dispatch. Implementations must not throw for an unhandled
    ///     target; they return <see cref="TransportStatus.Skipped" /> instead. A thrown exception
    ///     is isolated by the push service and surfaced as <see cref="TransportStatus.Failed" />.
    /// </summary>
    /// <param name="context">The dispatch context.</param>
    /// <param name="ct">A cancellation token.</param>
    ValueTask<TransportResult> TrySendAsync(PushContext context, CancellationToken ct = default);
}
