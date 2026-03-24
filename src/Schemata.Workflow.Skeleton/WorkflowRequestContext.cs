using Schemata.Workflow.Skeleton.Entities;

namespace Schemata.Workflow.Skeleton;

/// <summary>
/// Carries contextual information about a workflow request for use in access control decisions.
/// </summary>
/// <typeparam name="TRequest">The type of the request payload.</typeparam>
public class WorkflowRequestContext<TRequest>
{
    /// <summary>
    /// The name of the workflow operation being performed (e.g., Get, Submit, Raise).
    /// </summary>
    public string? Operation { get; set; }

    /// <summary>
    /// The request payload associated with this workflow operation.
    /// </summary>
    public TRequest? Request { get; set; }

    /// <summary>
    /// The workflow entity associated with this request, if available.
    /// </summary>
    public SchemataWorkflow? Workflow { get; set; }
}
