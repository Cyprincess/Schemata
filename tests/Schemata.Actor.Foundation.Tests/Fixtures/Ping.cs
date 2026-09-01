using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Foundation.Tests.Fixtures;

#region Messages

public sealed record Ping(string Text) : IRequest<string>;

#endregion

#region Actors

#endregion

#region DI scope probes

#endregion

#region Concurrency and lifecycle probes

#endregion