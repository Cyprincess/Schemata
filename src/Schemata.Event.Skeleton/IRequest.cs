namespace Schemata.Event.Skeleton;

/// <summary>
///     Marker interface for request payloads that expect a single
///     <typeparamref name="TResponse" /> through <see cref="IEventBus.SendAsync" />.
/// </summary>
public interface IRequest<TResponse> : IEvent;
