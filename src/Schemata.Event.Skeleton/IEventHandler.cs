using System.Threading;
using System.Threading.Tasks;

namespace Schemata.Event.Skeleton;

/// <summary>Handles events of type <typeparamref name="TEvent" />.</summary>
public interface IEventHandler<in TEvent>
    where TEvent : IEvent
{
    /// <summary>Processes <paramref name="event" />.</summary>
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
