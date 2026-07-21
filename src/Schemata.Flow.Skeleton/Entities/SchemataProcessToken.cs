using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>
///     A single execution token inside a <see cref="SchemataProcess" />. The state-machine engine
///     always keeps exactly one token; the BPMN engine grows / merges tokens across fork, join,
///     boundary catch, and sub-process scopes.
/// </summary>
[DisplayName("Token")]
[Table("SchemataProcessTokens")]
[CanonicalName("processes/{process}/tokens/{token}")]
[PrimaryKey(nameof(Uid))]
public class SchemataProcessToken : IIdentifier, ICanonicalName, IConcurrency, IStateful, ITimestamp, ISoftDelete,
                                    IAnnotatable
{
    /// <summary>Bare leaf id of the owning process (AIP structural parent, mode A).</summary>
    public virtual string Process { get; set; } = null!;

    /// <summary>
    ///     Full canonical name of the token that spawned this token (mode B cross-resource FK).
    ///     Named <c>Spawner</c> to leave the <see cref="IChild" /> <c>Parent</c> slot free for the
    ///     structural process parent. Always <see langword="null" /> under the state-machine engine.
    /// </summary>
    [ResourceReference(typeof(SchemataProcessToken))]
    public virtual string? Spawner { get; set; }

    /// <summary>
    ///     Owning scope key: the process instance name for the root scope (both engines), or the
    ///     SubProcess / Transaction / EventSubProcess element name for BPMN sub-scopes.
    /// </summary>
    public virtual string ScopeName { get; set; } = null!;

    /// <summary>Name of the element the token sits on.</summary>
    public virtual string StateName { get; set; } = null!;

    /// <summary>Name of the waiting element; non-null when the token is suspended at a catch event or boundary.</summary>
    public virtual string? WaitingAtName { get; set; }

    /// <summary>Engine-private counters used for loop and execution metadata.</summary>
    public virtual Dictionary<string, int> Bookkeeping { get; set; } = [];

    #region IAnnotatable Members

    public virtual Dictionary<string, string?> Annotations { get; set; } = [];

    #endregion

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

    #region ISoftDelete Members

    public virtual DateTime? DeleteTime { get; set; }

    public virtual DateTime? PurgeTime { get; set; }

    #endregion

    #region IStateful Members

    /// <summary>
    ///     Token lifecycle state. Allowed values: <c>Active</c> / <c>Waiting</c> / <c>Completed</c> /
    ///     <c>Failed</c> / <c>Cancelled</c> / <c>Compensating</c> / <c>Compensated</c>.
    /// </summary>
    public virtual string? State { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreateTime { get; set; }

    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
