using System.Collections.Generic;

namespace Schemata.Workflow.Skeleton.Models;

public class GraphResponse
{
    public virtual List<string>? Vertices { get; set; }

    public virtual List<EdgeResponse>? Edges { get; set; }
}
