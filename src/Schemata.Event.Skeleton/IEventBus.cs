using System;
using System.Threading;
using System.Threading.Tasks;
using Schemata.Abstractions.Entities;

namespace Schemata.Event.Skeleton;

/// <summary>
///     Event bus abstraction supporting fire-and-forget broadcast
///     (<see cref="PublishAsync{TEvent}(TEvent, CancellationToken)" />) and request/response
///     (<see cref="SendAsync" />).  Implementations may dispatch in-process or bridge to an
///     out-of-process transport.
/// </summary>
public interface IEventBus
{
    /// <summary>
    ///     Broadcasts <paramref name="event" /> to all subscribed handlers using payload metadata.
    /// </summary>
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
        where TEvent : IEvent;

    /// <summary>
    ///     Broadcasts <paramref name="event" /> with an optimistic-snapshot reference to
    ///     <paramref name="sourceEntity" />. The source entity must implement both
    ///     <see cref="ICanonicalName" /> and <see cref="IConcurrency" /> so consumers can
    ///     compare the event with the current state of the source row.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="event">The event instance.</param>
    /// <param name="sourceEntity">
    ///     The originating business entity. Must implement <see cref="ICanonicalName" /> and
    ///     <see cref="IConcurrency" />; otherwise an <see cref="InvalidOperationException" />
    ///     is thrown before the publish runs.
    /// </param>
    /// <param name="ct">A cancellation token.</param>
    Task PublishAsync<TEvent>(TEvent @event, object sourceEntity, CancellationToken ct = default)
        where TEvent : IEvent {
        EnsureSourceEntityContract(sourceEntity);
        return PublishAsync(@event, ct);
    }

    /// <summary>Dispatches <paramref name="request" /> to its single handler and returns the response.</summary>
    Task<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken ct = default)
        where TRequest : IRequest<TResponse>;

    /// <summary>
    ///     Validates that <paramref name="sourceEntity" /> satisfies the source-snapshot
    ///     contract (<see cref="ICanonicalName" /> + <see cref="IConcurrency" />). Throws
    ///     <see cref="InvalidOperationException" /> with the offending type name for invalid sources.
    /// </summary>
    /// <remarks>
    ///     The contract activates only when a publisher chooses to attach a source entity;
    ///     types that implement <see cref="ICanonicalName" /> for other reasons (DTOs,
    ///     request bodies, custom-method bindings) remain free of the <see cref="IConcurrency" />
    ///     requirement until they actually flow into Event/Flow/Scheduling as a source.
    /// </remarks>
    public static void EnsureSourceEntityContract(object sourceEntity) {
        ArgumentNullException.ThrowIfNull(sourceEntity);

        if (sourceEntity is not ICanonicalName) {
            throw new InvalidOperationException(
                $"Event source '{sourceEntity.GetType().FullName}' must implement ICanonicalName "
              + "to be used as a publish source.");
        }

        if (sourceEntity is not IConcurrency) {
            throw new InvalidOperationException(
                $"Event source '{sourceEntity.GetType().FullName}' must implement IConcurrency "
              + "so the framework can capture an optimistic-snapshot timestamp for consumers.");
        }
    }
}
