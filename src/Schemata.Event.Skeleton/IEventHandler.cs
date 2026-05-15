using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
