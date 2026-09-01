using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Scheduling.Tests.Fixtures;

#region Messages

public sealed record ReminderPayload(string Text) : IMessage;

#endregion

#region Actors

#endregion
