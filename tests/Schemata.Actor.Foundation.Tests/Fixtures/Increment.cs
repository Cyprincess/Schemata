using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

public sealed record Increment : IRequest<int>;