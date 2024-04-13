using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Models;

public class WorkflowResponse : IIdentifier, IStateful, ITimestamp
{
    public virtual WorkflowGraphResponse? Graph { get; set; }

    public virtual List<string>? Events { get; set; }

    public virtual List<TransitionResponse>? Transitions { get; set; }

    #region IIdentifier Members

    public virtual long Id { get; set; }

    #endregion

    #region IStateful Members

    public virtual string? State { get; set; }

    #endregion

    #region ITimestamp Members

    public virtual DateTime? CreationDate { get; set; }

    public virtual DateTime? ModificationDate { get; set; }

    #endregion
}
