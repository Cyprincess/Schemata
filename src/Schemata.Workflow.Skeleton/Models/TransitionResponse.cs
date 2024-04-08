using System;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Models;

public class TransitionResponse : IIdentifier, IEvent, ITimestamp
{
    public long WorkflowId { get; set; }

    public string? Previous { get; set; }

    public string? Posterior { get; set; }

    #region IEvent Members

    public string Event { get; set; } = null!;

    public string? Note { get; set; }

    public long? UpdatedById { get; set; }

    public string? UpdatedBy { get; set; }

    #endregion

    #region IIdentifier Members

    public long Id { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreationDate { get; set; }

    public DateTime? ModificationDate { get; set; }

    #endregion
}
