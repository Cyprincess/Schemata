using System.Collections.Generic;

namespace Schemata.Workflow.Skeleton.Models;

public class WorkflowGraphResponse
{
    public virtual List<string>? Vertices { get; set; }

    public virtual List<WorkflowEdgeResponse>? Edges { get; set; }
}
