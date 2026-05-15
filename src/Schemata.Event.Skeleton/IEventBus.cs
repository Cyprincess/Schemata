using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;
}
