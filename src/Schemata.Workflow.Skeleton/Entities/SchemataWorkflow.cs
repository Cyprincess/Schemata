using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Entities;

/// <summary>
///     Represents a workflow instance, linking to the underlying stateful entity by identifier and type.
/// </summary>
[DisplayName("Workflow")]
[Table("SchemataWorkflows")]
[CanonicalName("workflows/{workflow}")]
public class SchemataWorkflow : IIdentifier, ICanonicalName, ITimestamp
{
    /// <summary>
    ///     The identifier of the stateful entity instance that this workflow tracks.
    /// </summary>
    public virtual long InstanceId { get; set; }

    /// <summary>
    ///     The fully qualified CLR type name of the stateful entity instance.
    /// </summary>
    public virtual string InstanceType { get; set; } = null!;

    #region ICanonicalName Members

    /// <inheritdoc />
    public virtual string? Name { get; set; }

    /// <inheritdoc />
    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    /// <inheritdoc />
    [Key]
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    /// <inheritdoc />
    public virtual DateTime? CreateTime { get; set; }

    /// <inheritdoc />
    public virtual DateTime? UpdateTime { get; set; }

    #endregion
}
