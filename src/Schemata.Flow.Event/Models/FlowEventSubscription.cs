using Schemata.Event.Skeleton;

namespace Schemata.Flow.Event.Models;

/// <summary><see cref="IEventSubscription"/> implementation used by the Flow.Event bridge.</summary>
public sealed class FlowEventSubscription : IEventSubscription
{
    #region IEventSubscription Members

    public string Id { get; set; } = null!;

    public string EventType { get; set; } = null!;

    public string? CorrelationKey { get; set; }

    public string? Target { get; set; }

    #endregion
}
