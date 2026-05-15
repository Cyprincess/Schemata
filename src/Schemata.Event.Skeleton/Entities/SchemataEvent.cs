using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Event.Skeleton.Entities;

[DisplayName("Event")]
[Table("SchemataEvents")]
[CanonicalName("events/{event}")]
public class SchemataEvent : IIdentifier, ICanonicalName, ITimestamp
{
    public virtual string? EventType { get; set; }

    public virtual string? Payload { get; set; }

    public virtual EventState State { get; set; }

    public virtual string? CorrelationId { get; set; }

    public virtual string? ResponsePayload { get; set; }

    public virtual string? Exception { get; set; }

    public virtual int RetryCount { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    public virtual Guid Uid { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
