using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Entities;

[DisplayName("Workflow")]
[Table("SchemataWorkflows")]
[CanonicalName("workflows/{workflow}")]
public class SchemataWorkflow : IIdentifier, ICanonicalName, ITimestamp
{
    public virtual string Type { get; set; } = null!;

    public virtual long InstanceId { get; set; }

    public virtual string InstanceType { get; set; } = null!;

    #region ICanonicalName Members

    public virtual string? Name { get; set; }

    public virtual string? CanonicalName { get; set; }

    #endregion

    #region IIdentifier Members

    [Key]
    public virtual long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate { get; set; }

    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
