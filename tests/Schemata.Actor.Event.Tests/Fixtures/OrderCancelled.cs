using Schemata.Event.Skeleton;

namespace Schemata.Actor.Event.Tests.Fixtures;

public sealed record OrderCancelled(string OrderId) : IEvent;