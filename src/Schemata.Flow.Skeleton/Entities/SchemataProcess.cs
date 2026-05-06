using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Entity.Repository;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>
///     A persisted process instance that derives from a registered <see cref="Models.ProcessDefinition" />.
///     Tracks the current state, serialized variables, and the element it is waiting at.
/// </summary>
[DisplayName("Process")]
[Table("SchemataProcesses")]
[CanonicalName("processes/{process}")]
public class SchemataProcess : IIdentifier, ICanonicalName, IDescriptive, ISoftDelete, ITimestamp, IStateful
{
    /// <summary>
    ///     The <see cref="Models.ProcessDefinition.Name" /> of the definition
    ///     this instance is based on.
    /// </summary>
    public virtual string ProcessDefinitionName { get; set; } = null!;

    /// <summary>
    ///     Serialized process variables in JSON format. Deserialized by the
    ///     engine before each state transition and re-serialized afterward.
    /// </summary>
    public virtual string? Variables { get; set; }

    /// <summary>
    ///     The <see cref="Models.FlowElement.Id" /> of the current element
    ///     the process token is at.
    /// </summary>
    public virtual string? StateId { get; set; }

    /// <summary>
    ///     The <see cref="Models.FlowElement.Id" /> of the event or
    ///     <see cref="Models.EventBasedGateway" /> the instance is waiting at.
    ///     <c>null</c> when the instance is at an activity and can auto-advance.
    /// </summary>
    public virtual string? WaitingAtId { get; set; }

    /// <summary>
    ///     The <see cref="Models.FlowElement.Name" /> of the waiting element
    ///     (display label, derived from <see cref="WaitingAtId" />).
    /// </summary>
    public virtual string? WaitingAt { get; set; }

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IDescriptive Members

    public virtual string? DisplayName { get; set; }

    public virtual Dictionary<string, string>? DisplayNames { get; set; }

    public virtual string? Description { get; set; }

    public virtual Dictionary<string, string>? Descriptions { get; set; }

    #endregion

    #region IIdentifier Members

    [TableKey]
    public virtual Guid Uid { get; set; }

    #endregion

    #region ISoftDelete Members

    public DateTime? DeleteTime { get; set; }

    public DateTime? PurgeTime { get; set; }

    #endregion

    #region IStateful Members

    /// <summary>
    ///     The <see cref="Models.FlowElement.Name" /> of the current element
    ///     the process token is at.
    /// </summary>
    public virtual string? State { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
