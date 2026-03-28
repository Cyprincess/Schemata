namespace Schemata.Workflow.Skeleton.Models;

/// <summary>
///     Represents a directed edge (transition) in a workflow state graph.
/// </summary>
public class EdgeResponse
{
    /// <summary>
    ///     The target state of this edge.
    /// </summary>
    public virtual string? To { get; set; }

    /// <summary>
    ///     The source state of this edge.
    /// </summary>
    public virtual string? From { get; set; }
}
