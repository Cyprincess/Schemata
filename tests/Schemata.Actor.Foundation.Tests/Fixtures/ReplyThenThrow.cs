using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

public sealed record ReplyThenThrow(string Reply, string Message) : IRequest<string>;