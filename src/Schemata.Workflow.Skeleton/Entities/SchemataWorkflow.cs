using System;
using System.ComponentModel.DataAnnotations.Schema;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Entities;

[Table("Workflows")]
public class SchemataWorkflow : IIdentifier, ITimestamp
{
    public string Type { get; set; } = null!;

    public long InstanceId { get; set; }

    public string InstanceType { get; set; } = null!;

    #region IIdentifier Members

    public long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreationDate { get; set; }

    public DateTime? ModificationDate { get; set; }

    #endregion
}
