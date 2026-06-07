using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Event bus abstraction supporting fire-and-forget broadcast
///     (<see cref="PublishAsync" />) and request/response
///     (<see cref="SendAsync" />).  Implementations may dispatch in-process
///     or bridge to an out-of-process transport.
/// </summary>
public interface IEventBus
{
    /// <summary>Broadcasts <paramref name="event" /> to all subscribed handlers.</summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    /// <summary>Dispatches <paramref name="request" /> to its single handler and returns the response.</summary>
    Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;
}
