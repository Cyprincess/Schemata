using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Event.Skeleton.Entities;

/// <summary>Durable event subscription row used by event dispatchers and bridge features.</summary>
[DisplayName("EventSubscription")]
[Table("SchemataEventSubscriptions")]
[PrimaryKey(nameof(Uid))]
[CanonicalName("event-subscriptions/{event_subscription}")]
[Index(nameof(SubscriptionId), IsUnique = true)]
[Index(nameof(EventType))]
public class SchemataEventSubscription : IIdentifier, ICanonicalName, IConcurrency, ITimestamp
{
    /// <summary>Wire-format event name matched by this subscription.</summary>
    public virtual string EventType { get; set; } = null!;

    /// <summary>Optional correlation key for one-to-one delivery.</summary>
    public virtual string? CorrelationKey { get; set; }

    /// <summary>Subscription target identifier consumed by the delivering handler.</summary>
    public virtual string Target { get; set; } = null!;

    /// <summary>Canonical name of the token targeted by a message subscription.</summary>
    public virtual string? Token { get; set; }

    /// <summary>Stable subscription identifier; the unique lookup key used by the dispatcher.</summary>
    public virtual string SubscriptionId { get; set; } = null!;

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
