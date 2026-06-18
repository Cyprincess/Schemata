namespace Schemata.Event.Skeleton;

/// <summary>
///     Outcome of an <see cref="IEventOutboxPublisher" /> replay, telling the outbox dispatcher
///     whether it still owns the row's terminal state.
/// </summary>
public enum EventOutboxDelivery
{
    /// <summary>
    ///     The message was handed to the broker; a downstream consumer records and consumes it
    ///     later, so the dispatcher may mark the row delivered.
    /// </summary>
    Delivered,

    /// <summary>
    ///     The message was replayed through in-process handlers; the consume path already set the
    ///     terminal state (Succeeded or Failed), so the dispatcher must not overwrite it.
    /// </summary>
    Consumed,
}
