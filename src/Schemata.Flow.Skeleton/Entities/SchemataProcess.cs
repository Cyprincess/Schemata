using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>Persisted process instance derived from a registered <see cref="Models.ProcessDefinition" />.</summary>
[DisplayName("Process")]
[Table("SchemataProcesses")]
[CanonicalName("processes/{process}")]
[PrimaryKey(nameof(Uid))]
public class SchemataProcess : IIdentifier, ICanonicalName, IConcurrency, IDescriptive, ISourceReference,
                               ISoftDelete, ITimestamp, IStateful
{
    /// <summary>The <see cref="Models.ProcessDefinition.Name" /> of the source definition.</summary>
    public virtual string DefinitionName { get; set; } = null!;

    /// <summary>Serialized process variables in JSON format.</summary>
    public virtual string? Variables { get; set; }

    /// <summary>The <see cref="Models.FlowElement.Id" /> of the current element the token is at.</summary>
    public virtual string? StateId { get; set; }

    /// <summary>The <see cref="Models.FlowElement.Id" /> of the event or gateway the instance is waiting at.</summary>
    public virtual string? WaitingAtId { get; set; }

    /// <summary>The <see cref="Models.FlowElement.Name" /> of the waiting element.</summary>
    public virtual string? WaitingAt { get; set; }

    #region ISourceReference Members

    /// <inheritdoc />
    public virtual string? SourceType { get; set; }

    /// <inheritdoc />
    public virtual string? Source { get; set; }

    /// <inheritdoc />
    public virtual Guid? SourceTimestamp { get; set; }

    #endregion

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IConcurrency Members

    [ConcurrencyCheck]
    public virtual Guid Timestamp { get; set; }

    #endregion

    #region IDescriptive Members

    public virtual string? DisplayName { get; set; }

    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members
    public virtual Guid Uid { get; set; }

    #endregion

    #region ISoftDelete Members

    public DateTime? DeleteTime { get; set; }

    public DateTime? PurgeTime { get; set; }

    #endregion

    #region IStateful Members

    public virtual string? State { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
