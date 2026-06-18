using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Event.Skeleton.Entities;

/// <summary>Persisted audit record for an event published through <see cref="IEventBus" />.</summary>
[DisplayName("Event")]
[Table("SchemataEvents")]
[CanonicalName("events/{event}")]
public class SchemataEvent : IIdentifier, ICanonicalName, IConcurrency, ISourceReference, ITimestamp
{
    /// <summary>Wire-format event type name.</summary>
    public virtual string? EventType { get; set; }

    /// <summary>Serialized event body.</summary>
    public virtual string? Payload { get; set; }

    /// <summary>Lifecycle state of this audit record.</summary>
    public virtual EventState State { get; set; }

    /// <summary>Correlation identifier copied from <see cref="EventContext.CorrelationId" />.</summary>
    public virtual string? CorrelationId { get; set; }

    /// <summary>Serialized handler response (for <see cref="IEventBus.SendAsync" /> requests).</summary>
    public virtual string? ResponsePayload { get; set; }

    /// <summary>Last error reported by a failed handler dispatch.</summary>
    public virtual string? RecentError { get; set; }

    /// <summary>Number of dispatch retries the host has performed.</summary>
    public virtual int RetryCount { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region ISourceReference Members

    /// <inheritdoc />
    public virtual string? SourceType { get; set; }

    /// <inheritdoc />
    public virtual string? Source { get; set; }

    /// <inheritdoc />
    public virtual Guid? SourceTimestamp { get; set; }

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
