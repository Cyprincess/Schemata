using Schemata.Event.Skeleton;

namespace Schemata.Event.Integration.Tests.Fixtures;

internal sealed record StudentCreated(string StudentId) : IEvent;
