using System;
using System.Collections.Generic;
using Schemata.Abstractions.Entities;

namespace Schemata.Workflow.Skeleton.Models;

public class WorkflowResponse : IIdentifier, IStateful, ITimestamp
{
    public List<string>? Events { get; set; }

    public List<TransitionResponse>? Transitions { get; set; }

    #region IIdentifier Members

    public long Id { get; set; }

    #endregion

    #region IStateful Members

    public string? State { get; set; }

    #endregion

    #region ITimestamp Members

    public DateTime? CreationDate { get; set; }

    public DateTime? ModificationDate { get; set; }

    #endregion
}
