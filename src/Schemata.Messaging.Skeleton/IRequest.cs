namespace Schemata.Messaging.Skeleton;

/// <summary>
///     Marker interface for request payloads that expect a single
///     <typeparamref name="TResponse" /> through <see cref="IRequestDispatcher.SendAsync" />.
/// </summary>
/// <typeparam name="TResponse">The response produced by the request's single handler.</typeparam>
public interface IRequest<TResponse> : IMessage;
