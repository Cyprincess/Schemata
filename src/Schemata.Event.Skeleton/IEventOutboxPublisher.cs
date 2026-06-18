using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Low-level broker publisher used by the outbox dispatcher to replay a persisted event.
///     Unlike <see cref="IEventBus.PublishAsync{TEvent}(TEvent, System.Threading.CancellationToken)" />
///     it does not run the publish pipeline or write a new audit row, so retries do not duplicate
///     the outbox record. Provided by a transport that can lose a message after accepting a
///     publish (e.g. RabbitMQ).
/// </summary>
public interface IEventOutboxPublisher
{
    /// <summary>
    ///     Publishes <paramref name="message" />, completing on confirmation. The returned
    ///     <see cref="EventOutboxDelivery" /> tells the dispatcher whether the row's terminal state
    ///     is now owned by an in-process consume (<see cref="EventOutboxDelivery.Consumed" />) or
    ///     awaits a downstream consumer (<see cref="EventOutboxDelivery.Delivered" />).
    /// </summary>
    Task<EventOutboxDelivery> PublishAsync(EventOutboxMessage message, CancellationToken ct = default);
}
