using Schemata.Messaging.Skeleton;

namespace Schemata.Actor.Scheduling.Tests.Fixtures;

public sealed record GetReceived : IRequest<ReminderPayload?>;