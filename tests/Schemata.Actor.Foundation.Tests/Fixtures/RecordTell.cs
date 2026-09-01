using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

public sealed record RecordTell(string Value) : IMessage;