using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Event.Tests.Fixtures;

public sealed record GetReceived : IRequest<IMessage?>;