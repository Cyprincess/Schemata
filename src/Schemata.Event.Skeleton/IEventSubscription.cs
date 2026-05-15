namespace Schemata.Event.Skeleton;

public interface IEventSubscription
{
    string Id { get; }

    string EventType { get; }

    string? CorrelationKey { get; }

    string? Target { get; }
}
