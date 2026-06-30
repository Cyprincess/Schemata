using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Schemata.Abstractions.Entities;
using Schemata.Abstractions.Resource;

namespace Schemata.Flow.Skeleton.Entities;

/// <summary>
///     Source entity binding for a process or a specific token branch.
/// </summary>
[DisplayName("ProcessSource")]
[Table("SchemataProcessSources")]
[PrimaryKey(nameof(Uid))]
[Index(nameof(Process), nameof(Token), nameof(Name), IsUnique = true)]
public class SchemataProcessSource : IIdentifier, IConcurrency, ITimestamp
{
    /// <summary>Canonical name of the owning process.</summary>
    [ResourceReference(typeof(SchemataProcess))]
    public virtual string Process { get; set; } = null!;

    /// <summary>Canonical name of the owning token, or <see langword="null" /> for process-level bindings.</summary>
    [ResourceReference(typeof(SchemataProcessToken))]
    public virtual string? Token { get; set; }

    /// <summary>Flow binding name used by source-aware DSL and task context APIs.</summary>
    public virtual string Name { get; set; } = null!;

    /// <summary>CLR type name recorded for registry lookup and audit.</summary>
    public virtual string SourceType { get; set; } = null!;

    /// <summary>Canonical name of the bound source entity.</summary>
    [ResourceReference]
    public virtual string Source { get; set; } = null!;

    /// <summary>Concurrency timestamp observed on the bound source entity.</summary>
    public virtual Guid? SourceTimestamp { get; set; }

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
