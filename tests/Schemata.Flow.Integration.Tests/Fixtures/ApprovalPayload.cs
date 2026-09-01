using Schemata.Event.Skeleton;

namespace Schemata.Flow.Integration.Tests.Fixtures;

public sealed class ApprovalPayload : IEvent
{
    public bool Approved { get; init; }
}