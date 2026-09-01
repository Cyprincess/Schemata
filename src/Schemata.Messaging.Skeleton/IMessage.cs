namespace Schemata.Messaging.Skeleton;

/// <summary>
///     Root marker for a payload that crosses a messaging boundary: an event broadcast to many
///     handlers, a request answered by exactly one, or an actor mailbox payload.
/// </summary>
public interface IMessage;
