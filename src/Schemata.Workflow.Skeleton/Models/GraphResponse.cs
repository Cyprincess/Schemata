using System.Collections.Generic;

namespace Schemata.Workflow.Skeleton.Models;

/// <summary>
/// Represents the full state graph of a workflow, including all states and transitions.
/// </summary>
public class GraphResponse
{
    /// <summary>
    /// The states (vertices) in the graph.
    /// </summary>
    public virtual List<string>? Vertices { get; set; }

    /// <summary>
    /// The directed edges (transitions) connecting states in the graph.
    /// </summary>
    public virtual List<EdgeResponse>? Edges { get; set; }
}
